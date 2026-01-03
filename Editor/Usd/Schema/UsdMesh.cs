using System.Collections.Generic;

namespace Sandbox.Usd.Schema;

/// <summary>
/// USD Mesh primitive containing geometry data
/// </summary>
public class UsdMesh : UsdPrim
{
	public UsdMesh()
	{
		TypeName = "Mesh";
	}
	
	/// <summary>
	/// Purpose of this geometry (render, proxy, guide)
	/// "guide" meshes are helpers and should not be rendered
	/// </summary>
	public string Purpose
	{
		get
		{
			var attr = GetAttribute<UsdToken>( "purpose" );
			return attr?.Value ?? "default";
		}
	}
	
	/// <summary>
	/// Whether this mesh should be rendered (not a guide/helper)
	/// </summary>
	public bool IsRenderable => Purpose != "guide";
	
	/// <summary>
	/// Vertex positions
	/// </summary>
	public List<Vector3> Points
	{
		get
		{
			var attr = GetAttribute<UsdVector3Array>( "points" );
			return attr?.Values ?? new List<Vector3>();
		}
	}
	
	/// <summary>
	/// Face vertex counts (e.g., [3, 3, 3] for triangles, [4, 4] for quads)
	/// </summary>
	public List<int> FaceVertexCounts
	{
		get
		{
			var attr = GetAttribute<UsdIntArray>( "faceVertexCounts" );
			return attr?.Values ?? new List<int>();
		}
	}
	
	/// <summary>
	/// Face vertex indices
	/// </summary>
	public List<int> FaceVertexIndices
	{
		get
		{
			var attr = GetAttribute<UsdIntArray>( "faceVertexIndices" );
			return attr?.Values ?? new List<int>();
		}
	}
	
	/// <summary>
	/// Normals (may be per-vertex or per-face-vertex depending on interpolation)
	/// </summary>
	public List<Vector3> Normals
	{
		get
		{
			var attr = GetAttribute<UsdVector3Array>( "primvars:normals" );
			return attr?.Values ?? new List<Vector3>();
		}
	}
	
	/// <summary>
	/// Normal indices (for indexed normals)
	/// </summary>
	public List<int> NormalIndices
	{
		get
		{
			var attr = GetAttribute<UsdIntArray>( "primvars:normals:indices" );
			return attr?.Values ?? new List<int>();
		}
	}
	
	/// <summary>
	/// UV coordinates
	/// </summary>
	public List<Vector2> UVs
	{
		get
		{
			var attr = GetAttribute<UsdVector2Array>( "primvars:st" );
			return attr?.Values ?? new List<Vector2>();
		}
	}
	
	/// <summary>
	/// UV indices (for indexed UVs)
	/// </summary>
	public List<int> UVIndices
	{
		get
		{
			var attr = GetAttribute<UsdIntArray>( "primvars:st:indices" );
			return attr?.Values ?? new List<int>();
		}
	}
	
	/// <summary>
	/// Mesh extent (bounding box)
	/// </summary>
	public (Vector3 Min, Vector3 Max)? Extent
	{
		get
		{
			var attr = GetAttribute<UsdVector3Array>( "extent" );
			if ( attr != null && attr.Count >= 2 )
			{
				return (attr[0], attr[1]);
			}
			return null;
		}
	}
	
	/// <summary>
	/// Orientation (leftHanded or rightHanded)
	/// </summary>
	public string Orientation
	{
		get
		{
			var attr = GetAttribute<UsdToken>( "orientation" );
			return attr?.Value ?? "rightHanded";
		}
	}
	
	/// <summary>
	/// Subdivision scheme (none, catmullClark, etc.)
	/// </summary>
	public string SubdivisionScheme
	{
		get
		{
			var attr = GetAttribute<UsdToken>( "subdivisionScheme" );
			return attr?.Value ?? "catmullClark";
		}
	}
	
	/// <summary>
	/// Path to skeleton for skinned meshes (skel:skeleton relationship)
	/// </summary>
	public string SkeletonPath
	{
		get
		{
			// Try to find the skel:skeleton relationship in relationships
			foreach ( var rel in Relationships )
			{
				if ( rel.Name == "skel:skeleton" && rel.Targets.Count > 0 )
				{
					return rel.Targets[0];
				}
			}
			return null;
		}
	}
	
	/// <summary>
	/// Joint indices for skeleton binding (primvars:skel:jointIndices)
	/// </summary>
	public List<int> JointIndices
	{
		get
		{
			var attr = GetAttribute<UsdIntArray>( "primvars:skel:jointIndices" );
			return attr?.Values ?? new List<int>();
		}
	}
	
	/// <summary>
	/// Joint weights for skeleton binding (primvars:skel:jointWeights)
	/// </summary>
	public List<float> JointWeights
	{
		get
		{
			var attr = GetAttribute<UsdFloatArray>( "primvars:skel:jointWeights" );
			return attr?.Values ?? new List<float>();
		}
	}
	
	/// <summary>
	/// Whether this mesh is bound to a skeleton
	/// </summary>
	public bool HasSkeletonBinding => !string.IsNullOrEmpty( SkeletonPath ) && JointIndices.Count > 0;
	
	/// <summary>
	/// Expand indexed normals to per-face-vertex, or calculate flat normals if missing
	/// </summary>
	public List<Vector3> GetExpandedNormals()
	{
		var normals = Normals;
		var indices = NormalIndices;
		
		// If no normals at all, calculate flat normals from geometry (matches USDView)
		if ( normals.Count == 0 )
		{
			return CalculateFlatNormals();
		}
		
		if ( indices.Count == 0 )
			return normals;
		
		var result = new List<Vector3>( indices.Count );
		foreach ( var idx in indices )
		{
			if ( idx >= 0 && idx < normals.Count )
				result.Add( normals[idx] );
			else
				result.Add( Vector3.Up );
		}
		return result;
	}
	
	/// <summary>
	/// Flat normals - each vertex gets its face normal (no smoothing, matches USDView)
	/// </summary>
	private List<Vector3> CalculateFlatNormals()
	{
		var points = Points;
		var faceVertexCounts = FaceVertexCounts;
		var faceVertexIndices = FaceVertexIndices;
		
		if ( points.Count == 0 || faceVertexIndices.Count == 0 )
			return new List<Vector3>();
		
		var result = new List<Vector3>( faceVertexIndices.Count );
		
		int indexOffset = 0;
		foreach ( var vertCount in faceVertexCounts )
		{
			if ( vertCount < 3 )
			{
				// Not enough vertices for a face, use default normal
				for ( int i = 0; i < vertCount; i++ )
					result.Add( new Vector3( 0, 0, 1 ) );
				indexOffset += vertCount;
				continue;
			}
			
			// Get first 3 vertex positions for face normal calculation
			int idx0 = faceVertexIndices[indexOffset];
			int idx1 = faceVertexIndices[indexOffset + 1];
			int idx2 = faceVertexIndices[indexOffset + 2];
			
			Vector3 faceNormal = new Vector3( 0, 0, 1 );
			
			if ( idx0 >= 0 && idx0 < points.Count && 
			     idx1 >= 0 && idx1 < points.Count && 
			     idx2 >= 0 && idx2 < points.Count )
			{
				var p0 = points[idx0];
				var p1 = points[idx1];
				var p2 = points[idx2];
				
				var edge1 = new Vector3( p1.x - p0.x, p1.y - p0.y, p1.z - p0.z );
				var edge2 = new Vector3( p2.x - p0.x, p2.y - p0.y, p2.z - p0.z );
				
				// Cross product
				faceNormal = new Vector3(
					edge1.y * edge2.z - edge1.z * edge2.y,
					edge1.z * edge2.x - edge1.x * edge2.z,
					edge1.x * edge2.y - edge1.y * edge2.x
				);
				
				// Normalize
				var len = (float)System.Math.Sqrt( faceNormal.x * faceNormal.x + faceNormal.y * faceNormal.y + faceNormal.z * faceNormal.z );
				if ( len > 0.0001f )
					faceNormal = new Vector3( faceNormal.x / len, faceNormal.y / len, faceNormal.z / len );
				else
					faceNormal = new Vector3( 0, 0, 1 );
			}
			
			// All vertices of this face get the same face normal (flat shading)
			for ( int i = 0; i < vertCount; i++ )
				result.Add( faceNormal );
			
			indexOffset += vertCount;
		}
		
		return result;
	}
	
	/// <summary>
	/// Convert indexed face-varying UVs to per-face-vertex data
	/// </summary>
	public List<Vector2> GetExpandedUVs()
	{
		var uvs = UVs;
		var indices = UVIndices;
		
		if ( indices.Count == 0 )
			return uvs;
		
		var result = new List<Vector2>( indices.Count );
		foreach ( var idx in indices )
		{
			if ( idx >= 0 && idx < uvs.Count )
				result.Add( uvs[idx] );
			else
				result.Add( Vector2.Zero );
		}
		return result;
	}
	
	/// <summary>
	/// Triangulate the mesh (convert quads and n-gons to triangles)
	/// Returns triangle indices into the original faceVertexIndices
	/// </summary>
	public List<int> GetTriangulatedIndices()
	{
		var result = new List<int>();
		var faceVertexCounts = FaceVertexCounts;
		var faceVertexIndices = FaceVertexIndices;
		
		int indexOffset = 0;
		foreach ( var vertexCount in faceVertexCounts )
		{
			if ( vertexCount < 3 )
			{
				indexOffset += vertexCount;
				continue;
			}
			
			// Fan triangulation: (0, 1, 2), (0, 2, 3), (0, 3, 4), ...
			for ( int i = 1; i < vertexCount - 1; i++ )
			{
				result.Add( faceVertexIndices[indexOffset] );
				result.Add( faceVertexIndices[indexOffset + i] );
				result.Add( faceVertexIndices[indexOffset + i + 1] );
			}
			
			indexOffset += vertexCount;
		}
		
		return result;
	}
	
	/// <summary>
	/// Get triangulated face-vertex indices (indices into the faceVertexIndices array, for expanding normals/UVs)
	/// </summary>
	public List<int> GetTriangulatedFaceVertexIndices()
	{
		var result = new List<int>();
		var faceVertexCounts = FaceVertexCounts;
		
		int indexOffset = 0;
		foreach ( var vertexCount in faceVertexCounts )
		{
			if ( vertexCount < 3 )
			{
				indexOffset += vertexCount;
				continue;
			}
			
			// Fan triangulation
			for ( int i = 1; i < vertexCount - 1; i++ )
			{
				result.Add( indexOffset );
				result.Add( indexOffset + i );
				result.Add( indexOffset + i + 1 );
			}
			
			indexOffset += vertexCount;
		}
		
		return result;
	}
}

