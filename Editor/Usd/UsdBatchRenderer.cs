using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Sandbox.Usd;

/// <summary>
/// A component that renders USD instances using true GPU instancing.
/// Uses SceneCustomObject with Graphics.DrawModelInstanced to render all instances.
/// </summary>
public class UsdBatchRenderer : Component
{
	/// <summary>
	/// Instance data grouped by model
	/// </summary>
	private readonly Dictionary<Model, List<Transform>> _instanceGroups = new();
	
	/// <summary>
	/// Pre-computed transform arrays for rendering
	/// </summary>
	private readonly Dictionary<Model, Transform[]> _transformArrays = new();
	
	/// <summary>
	/// Custom scene object for rendering
	/// </summary>
	private SceneCustomObject _sceneObject;
	
	/// <summary>
	/// Total instance count
	/// </summary>
	public int TotalInstances { get; private set; }
	
	/// <summary>
	/// Number of unique models (draw calls)
	/// </summary>
	public int UniqueModels => _transformArrays.Count;
	
	/// <summary>
	/// Add an instance to be rendered
	/// </summary>
	public void AddInstance( Model model, Transform transform )
	{
		if ( model == null || !model.IsValid() )
			return;
		
		if ( !_instanceGroups.TryGetValue( model, out var list ) )
		{
			list = new List<Transform>();
			_instanceGroups[model] = list;
		}
		
		list.Add( transform );
	}
	
	/// <summary>
	/// Finalize instance data - call after adding all instances
	/// </summary>
	public void Build()
	{
		_transformArrays.Clear();
		TotalInstances = 0;
		
		foreach ( var kvp in _instanceGroups )
		{
			var transforms = kvp.Value.ToArray();
			_transformArrays[kvp.Key] = transforms;
			TotalInstances += transforms.Length;
		}
		
		// Clear the source lists to free memory
		_instanceGroups.Clear();
		
		Log.Info( $"UsdBatchRenderer: Built {UniqueModels} model groups with {TotalInstances} total instances" );
		
		// Sample output
		int count = 0;
		foreach ( var kvp in _transformArrays )
		{
			if ( count >= 3 ) break;
			Log.Info( $"  Model: {kvp.Key.ResourceName}, Instances: {kvp.Value.Length}" );
			count++;
		}
		
		// Create the scene object for rendering
		CreateSceneObject();
	}
	
	private void CreateSceneObject()
	{
		if ( Scene?.SceneWorld == null )
		{
			Log.Warning( "UsdBatchRenderer: No valid SceneWorld" );
			return;
		}
		
		_sceneObject = new SceneCustomObject( Scene.SceneWorld );
		_sceneObject.RenderOverride = RenderInstances;
		_sceneObject.Flags.CastShadows = true;
		_sceneObject.RenderingEnabled = true;
		
		Log.Info( $"UsdBatchRenderer: SceneCustomObject created" );
	}
	
	private void RenderInstances( SceneObject obj )
	{
		foreach ( var kvp in _transformArrays )
		{
			var model = kvp.Key;
			var transforms = kvp.Value;
			
			if ( model == null || !model.IsValid() || transforms.Length == 0 )
				continue;
			
			Graphics.DrawModelInstanced( model, transforms );
		}
	}
	
	protected override void OnEnabled()
	{
		base.OnEnabled();
		
		// If we already have data, create the scene object
		if ( _transformArrays.Count > 0 && _sceneObject == null )
		{
			CreateSceneObject();
		}
	}
	
	protected override void OnDisabled()
	{
		if ( _sceneObject != null && _sceneObject.IsValid() )
		{
			_sceneObject.Delete();
			_sceneObject = null;
		}
		
		base.OnDisabled();
	}
	
	protected override void OnDestroy()
	{
		if ( _sceneObject != null && _sceneObject.IsValid() )
		{
			_sceneObject.Delete();
			_sceneObject = null;
		}
		
		_transformArrays.Clear();
		_instanceGroups.Clear();
		
		base.OnDestroy();
	}
}

