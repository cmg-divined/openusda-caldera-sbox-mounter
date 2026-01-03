using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Sandbox.Usd;

/// <summary>
/// Renders USD instances using SceneObjects directly (bypassing GameObject overhead).
/// Creates one SceneObject per instance - lighter than GameObject but still individual objects.
/// For truly massive scenes, this is still heavy but much lighter than full GameObjects.
/// </summary>
public class UsdInstancedSceneRenderer : IDisposable
{
	/// <summary>
	/// All created scene objects
	/// </summary>
	private readonly List<SceneObject> _sceneObjects = new();
	
	/// <summary>
	/// The scene world we're rendering into
	/// </summary>
	private SceneWorld _sceneWorld;
	
	/// <summary>
	/// Pending instances to create
	/// </summary>
	private readonly List<(Model model, Transform transform)> _pendingInstances = new();
	
	/// <summary>
	/// Total number of instances
	/// </summary>
	public int TotalInstances => _sceneObjects.Count;
	
	/// <summary>
	/// Create a new instanced renderer for the given scene world
	/// </summary>
	public UsdInstancedSceneRenderer( SceneWorld sceneWorld )
	{
		_sceneWorld = sceneWorld;
		Log.Info( $"UsdInstancedSceneRenderer: Created with SceneWorld valid={sceneWorld?.IsValid()}" );
	}
	
	/// <summary>
	/// Add an instance to be rendered (deferred - call Build() later)
	/// </summary>
	public void AddInstance( Model model, Transform transform )
	{
		if ( model == null || !model.IsValid() )
			return;
		
		_pendingInstances.Add( (model, transform) );
	}
	
	/// <summary>
	/// Add an instance and create the SceneObject immediately.
	/// Use this for progressive loading where objects appear as they're processed.
	/// </summary>
	public void AddInstanceImmediate( Model model, Transform transform )
	{
		if ( model == null || !model.IsValid() )
			return;
		
		if ( _sceneWorld == null || !_sceneWorld.IsValid() )
			return;
		
		try
		{
			var so = new SceneObject( _sceneWorld, model, transform );
			so.Flags.CastShadows = true;
			_sceneObjects.Add( so );
		}
		catch
		{
			// Silently fail for immediate mode - errors logged elsewhere
		}
	}
	
	/// <summary>
	/// Create all the SceneObjects. Call this after adding all instances.
	/// </summary>
	public void Build()
	{
		if ( _sceneWorld == null || !_sceneWorld.IsValid() )
		{
			Log.Warning( "UsdInstancedSceneRenderer: Invalid scene world" );
			return;
		}
		
		Log.Info( $"UsdInstancedSceneRenderer: Creating {_pendingInstances.Count} SceneObjects..." );
		
		int created = 0;
		int failed = 0;
		
		foreach ( var (model, transform) in _pendingInstances )
		{
			try
			{
				var so = new SceneObject( _sceneWorld, model, transform );
				so.Flags.CastShadows = true;
				_sceneObjects.Add( so );
				created++;
				
				// Log first few
				if ( created <= 3 )
				{
					Log.Info( $"  Created SceneObject #{created}: {model.ResourceName} at {transform.Position}" );
				}
				
				// Progress every 5000
				if ( created % 5000 == 0 )
				{
					Log.Info( $"  Progress: {created}/{_pendingInstances.Count} SceneObjects created" );
				}
			}
			catch ( Exception ex )
			{
				failed++;
				if ( failed <= 3 )
				{
					Log.Warning( $"  Failed to create SceneObject: {ex.Message}" );
				}
			}
		}
		
		_pendingInstances.Clear();
		
		Log.Info( $"UsdInstancedSceneRenderer: Created {created} SceneObjects, {failed} failed" );
		Log.Info( $"UsdInstancedSceneRenderer: Ready!" );
	}
	
	/// <summary>
	/// Clear all instances and release resources
	/// </summary>
	public void Clear()
	{
		foreach ( var so in _sceneObjects )
		{
			if ( so.IsValid() )
				so.Delete();
		}
		_sceneObjects.Clear();
		_pendingInstances.Clear();
	}
	
	public void Dispose()
	{
		Clear();
		_sceneWorld = null;
	}
}

