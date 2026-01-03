using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox.Mounting;
using Sandbox.Usd.Parser;
using Sandbox.Usd.Schema;

namespace Sandbox.Usd;

/// <summary>
/// ResourceLoader for USD geometry files
/// </summary>
internal class UsdModelResourceLoader : ResourceLoader<UsdMount>
{
	/// <summary>
	/// Full path to the USD file
	/// </summary>
	public string FullPath { get; set; }
	
	protected override object Load()
	{
		try
		{
			var source = File.ReadAllText( FullPath );
			var parser = new UsdaParser( source, FullPath );
			var stage = parser.Parse();
			stage.FilePath = FullPath;
			
			return CreateModelFromStage( stage, System.IO.Path.GetFileNameWithoutExtension( FullPath ) );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Failed to load USD model {Path}: {ex.Message}" );
			return null;
		}
	}
	
	/// <summary>
	/// Create a Model from a parsed USD stage
	/// </summary>
	private Model CreateModelFromStage( UsdStage stage, string name )
	{
		var builder = Model.Builder.WithName( name );
		var allVertices = new List<Vector3>();
		var allIndices = new List<int>();
		var meshCount = 0;
		
		foreach ( var prim in stage.Traverse() )
		{
			if ( prim.TypeName != "Mesh" )
				continue;
			
			// Convert prim to UsdMesh if needed
			var mesh = prim as UsdMesh;
			if ( mesh == null )
			{
				mesh = ConvertToMesh( prim );
			}
			
			if ( mesh == null )
				continue;
			
			var sboxMesh = CreateMesh( mesh, allVertices, allIndices );
			if ( sboxMesh != null )
			{
				builder.AddMesh( sboxMesh );
				meshCount++;
			}
		}
		
		if ( meshCount == 0 )
		{
			Log.Warning( $"No meshes found in USD stage: {name}" );
			return null;
		}
		
		// Add trace mesh for collision
		if ( allVertices.Count > 0 && allIndices.Count > 0 )
		{
			builder.AddTraceMesh( allVertices, allIndices );
		}
		
		return builder.Create();
	}
	
	/// <summary>
	/// Convert a generic UsdPrim with Mesh attributes to a UsdMesh
	/// </summary>
	private static UsdMesh ConvertToMesh( UsdPrim prim )
	{
		var mesh = new UsdMesh
		{
			Name = prim.Name,
			Path = prim.Path,
			TypeName = "Mesh",
			Specifier = prim.Specifier,
			Parent = prim.Parent
		};
		
		foreach ( var attr in prim.Attributes )
		{
			mesh.Attributes[attr.Key] = attr.Value;
		}
		
		foreach ( var meta in prim.Metadata )
		{
			mesh.Metadata[meta.Key] = meta.Value;
		}
		
		return mesh;
	}
	
	/// <summary>
	/// Create an s&box Mesh from USD mesh data
	/// </summary>
	private Mesh CreateMesh( UsdMesh usdMesh, List<Vector3> traceVertices, List<int> traceIndices )
	{
		var points = usdMesh.Points;
		if ( points.Count == 0 )
			return null;
		
		var faceVertexIndices = usdMesh.FaceVertexIndices;
		if ( faceVertexIndices.Count == 0 )
			return null;
		
		// Get triangulated indices
		var triangleIndices = usdMesh.GetTriangulatedIndices();
		var faceVertexTriIndices = usdMesh.GetTriangulatedFaceVertexIndices();
		
		// Get expanded normals and UVs (per-face-vertex)
		var expandedNormals = usdMesh.GetExpandedNormals();
		var expandedUVs = usdMesh.GetExpandedUVs();
		
		// Skeleton-bound and Maya-style meshes have verts at bind pose, not origin
		bool shouldCenter = usdMesh.HasSkeletonBinding;
		Vector3 usdCenter = Vector3.Zero;
		
		// Maya exports: polySurfaceShape###, pPlaneShape###, geoShape###
		if ( !shouldCenter )
		{
			// Maya exports: polySurfaceShape###, pPlaneShape###, geoShape###
			bool isMayaBindPoseMesh = 
				System.Text.RegularExpressions.Regex.IsMatch( usdMesh.Name, @"^polySurfaceShape\d*$" ) ||
				System.Text.RegularExpressions.Regex.IsMatch( usdMesh.Name, @"^pPlaneShape\d*$" ) ||
				System.Text.RegularExpressions.Regex.IsMatch( usdMesh.Name, @"^geoShape\d*$" );
			
			if ( isMayaBindPoseMesh && usdMesh.Extent.HasValue )
			{
				var min = usdMesh.Extent.Value.Min;
				var max = usdMesh.Extent.Value.Max;
				float centerY = (min.y + max.y) / 2f;
				float centerX = (min.x + max.x) / 2f;
				if ( Math.Abs( centerY ) > 10f || Math.Abs( centerX ) > 10f )
					shouldCenter = true;
			}
		}
		
		if ( shouldCenter )
		{
			if ( usdMesh.Extent.HasValue )
			{
				var min = usdMesh.Extent.Value.Min;
				var max = usdMesh.Extent.Value.Max;
				usdCenter = new Vector3(
					(min.x + max.x) / 2f,
					(min.y + max.y) / 2f,
					0f  // Do NOT center on Z - preserves correct floor placement
				);
			}
			else if ( points.Count > 0 )
			{
				// No extent stored, compute from verts
				float minX = float.MaxValue, maxX = float.MinValue;
				float minY = float.MaxValue, maxY = float.MinValue;
				foreach ( var p in points )
				{
					if ( p.x < minX ) minX = p.x;
					if ( p.x > maxX ) maxX = p.x;
					if ( p.y < minY ) minY = p.y;
					if ( p.y > maxY ) maxY = p.y;
				}
				usdCenter = new Vector3(
					(minX + maxX) / 2f,
					(minY + maxY) / 2f,
					0f  // Do NOT center on Z - preserves correct floor placement
				);
			}
		}
		
		// Build vertex list with proper attributes
		var vertices = new List<SimpleVertex>();
		var indices = new List<int>();
		var vertexMap = new Dictionary<(int pointIdx, int faceVertIdx), int>();
		
		// Track trace mesh data
		int traceVertexOffset = traceVertices.Count;
		
		for ( int i = 0; i < triangleIndices.Count; i++ )
		{
			int pointIdx = triangleIndices[i];
			int faceVertIdx = faceVertexTriIndices[i];
			
			var usdPosition = points[pointIdx];
			
			// Subtract center before coordinate conversion (for skeleton-bound meshes)
			if ( shouldCenter )
			{
				usdPosition = new Vector3(
					usdPosition.x - usdCenter.x,
					usdPosition.y - usdCenter.y,
					usdPosition.z - usdCenter.z
				);
			}
			
			// Convert from USD coordinate system (Y-forward) to s&box (X-forward)
			// USD: X=right, Y=forward, Z=up -> s&box: X=forward, Y=right, Z=up
			// Negate X component to convert reflection to rotation (preserves handedness)
			var position = new Vector3( usdPosition.y, -usdPosition.x, usdPosition.z );
			
			Vector3 normal = Vector3.Up;
			if ( faceVertIdx < expandedNormals.Count )
			{
				var usdNormal = expandedNormals[faceVertIdx];
				// Same coordinate conversion for normals (including negation)
				normal = new Vector3( usdNormal.y, -usdNormal.x, usdNormal.z );
			}
			
			Vector2 uv = Vector2.Zero;
			if ( faceVertIdx < expandedUVs.Count )
				uv = expandedUVs[faceVertIdx];
			
			// Check if we already have this vertex
			var key = (pointIdx, faceVertIdx);
			if ( !vertexMap.TryGetValue( key, out int vertexIndex ) )
			{
				vertexIndex = vertices.Count;
				vertices.Add( new SimpleVertex( position, normal, Vector3.Zero, uv ) );
				vertexMap[key] = vertexIndex;
			}
			
			indices.Add( vertexIndex );
		}
		
		if ( vertices.Count == 0 )
			return null;
		
		// Note: No winding order reversal needed - the negation in position conversion
		// makes this a proper rotation (det=+1) instead of a reflection (det=-1)
		
		// Add to trace mesh
		foreach ( var v in vertices )
		{
			traceVertices.Add( v.position );
		}
		foreach ( var idx in indices )
		{
			traceIndices.Add( traceVertexOffset + idx );
		}
		
		// Create the mesh with simple_color shader (like Quake mount)
		var material = Material.Create( "model", "simple_color" );
		material?.Set( "Color", Texture.White );
		var mesh = new Mesh( material );
		
		// Calculate bounds
		var bounds = BBox.FromPoints( vertices.Select( v => v.position ) );
		mesh.Bounds = bounds;
		
		// Create buffers
		mesh.CreateVertexBuffer( vertices.Count, SimpleVertex.Layout, vertices );
		mesh.CreateIndexBuffer( indices.Count, indices );
		
		return mesh;
	}
}

