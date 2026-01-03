using System.Collections.Generic;

namespace Sandbox.Usd.Schema;

/// <summary>
/// Represents a USD Stage - the root container for a USD scene
/// </summary>
public class UsdStage
{
	/// <summary>
	/// The file path this stage was loaded from
	/// </summary>
	public string FilePath { get; set; }
	
	/// <summary>
	/// Root prims in the stage
	/// </summary>
	public List<UsdPrim> RootPrims { get; } = new();
	
	/// <summary>
	/// Default prim name (from metadata)
	/// </summary>
	public string DefaultPrim { get; set; }
	
	/// <summary>
	/// Up axis (Y or Z)
	/// </summary>
	public string UpAxis { get; set; } = "Y";
	
	/// <summary>
	/// Meters per unit (e.g., 0.01 for cm, 0.0254 for inches)
	/// </summary>
	public double MetersPerUnit { get; set; } = 0.01;
	
	/// <summary>
	/// Time codes per second
	/// </summary>
	public double TimeCodesPerSecond { get; set; } = 24.0;
	
	/// <summary>
	/// Frames per second
	/// </summary>
	public double FramesPerSecond { get; set; } = 24.0;
	
	/// <summary>
	/// Start time code
	/// </summary>
	public double StartTimeCode { get; set; } = 0;
	
	/// <summary>
	/// End time code
	/// </summary>
	public double EndTimeCode { get; set; } = 0;
	
	/// <summary>
	/// Sub-layers (other USD files that are composed into this stage)
	/// </summary>
	public List<string> SubLayers { get; } = new();
	
	/// <summary>
	/// Documentation/comment from the stage metadata
	/// </summary>
	public string Documentation { get; set; }
	
	/// <summary>
	/// All prims in the stage, indexed by path
	/// </summary>
	public Dictionary<string, UsdPrim> PrimsByPath { get; } = new();
	
	/// <summary>
	/// Get a prim by its path
	/// </summary>
	public UsdPrim GetPrimAtPath( string path )
	{
		PrimsByPath.TryGetValue( path, out var prim );
		return prim;
	}
	
	/// <summary>
	/// Get the default prim
	/// </summary>
	public UsdPrim GetDefaultPrim()
	{
		if ( string.IsNullOrEmpty( DefaultPrim ) )
			return RootPrims.Count > 0 ? RootPrims[0] : null;
		
		return GetPrimAtPath( $"/{DefaultPrim}" );
	}
	
	/// <summary>
	/// Traverse all prims in the stage
	/// </summary>
	public IEnumerable<UsdPrim> Traverse()
	{
		foreach ( var root in RootPrims )
		{
			foreach ( var prim in TraversePrim( root ) )
			{
				yield return prim;
			}
		}
	}
	
	private IEnumerable<UsdPrim> TraversePrim( UsdPrim prim )
	{
		yield return prim;
		foreach ( var child in prim.Children )
		{
			foreach ( var descendant in TraversePrim( child ) )
			{
				yield return descendant;
			}
		}
	}
	
	/// <summary>
	/// Find all prims of a specific type
	/// </summary>
	public IEnumerable<T> GetPrimsOfType<T>() where T : UsdPrim
	{
		foreach ( var prim in Traverse() )
		{
			if ( prim is T typed )
				yield return typed;
		}
	}
	
	/// <summary>
	/// Find all mesh prims
	/// </summary>
	public IEnumerable<UsdMesh> GetMeshes()
	{
		foreach ( var prim in Traverse() )
		{
			if ( prim.TypeName == "Mesh" && prim is UsdMesh mesh )
				yield return mesh;
		}
	}
	
	/// <summary>
	/// Register a prim in the path index
	/// </summary>
	public void RegisterPrim( UsdPrim prim )
	{
		if ( !string.IsNullOrEmpty( prim.Path ) )
		{
			PrimsByPath[prim.Path] = prim;
		}
	}
}



