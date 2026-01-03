using System;
using System.Collections.Generic;
using System.IO;

namespace Sandbox.Usd;

/// <summary>
/// Represents a mesh reference from the pre-built index
/// </summary>
public struct IndexedMeshRef
{
	/// <summary>
	/// Path to the source .geo.usda file containing this mesh
	/// </summary>
	public string SourcePath;
	
	/// <summary>
	/// Name of the mesh prim
	/// </summary>
	public string MeshName;
	
	/// <summary>
	/// Full path within the source file
	/// </summary>
	public string MeshPath;
	
	/// <summary>
	/// World position (converted from USD to s&box coordinates on load)
	/// </summary>
	public Vector3 Position;
	
	/// <summary>
	/// World rotation as quaternion
	/// </summary>
	public Rotation Rotation;
	
	/// <summary>
	/// World scale
	/// </summary>
	public Vector3 Scale;
	
	/// <summary>
	/// Whether this mesh has skeleton binding
	/// </summary>
	public bool HasSkeleton;
	
	/// <summary>
	/// Whether extent data is available
	/// </summary>
	public bool HasExtent;
	
	/// <summary>
	/// Minimum extent (if available)
	/// </summary>
	public Vector3 ExtentMin;
	
	/// <summary>
	/// Maximum extent (if available)
	/// </summary>
	public Vector3 ExtentMax;
	
	/// <summary>
	/// Get the world transform for this mesh
	/// </summary>
	public Transform WorldTransform => new Transform( Position, Rotation, Scale );
}

/// <summary>
/// Reads and provides access to a pre-built .usdi scene index
/// </summary>
public class UsdSceneIndex
{
	private const string MAGIC = "USDI";
	
	/// <summary>
	/// All source files referenced in the index
	/// </summary>
	public List<string> SourceFiles { get; } = new();
	
	/// <summary>
	/// All mesh references in the index
	/// </summary>
	public List<IndexedMeshRef> Meshes { get; } = new();
	
	/// <summary>
	/// Index file version
	/// </summary>
	public int Version { get; private set; }
	
	/// <summary>
	/// Load a .usdi index file
	/// </summary>
	public static UsdSceneIndex Load( string path )
	{
		var index = new UsdSceneIndex();
		
		using var stream = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536 );
		using var reader = new BinaryReader( stream );
		
		// Read and validate magic
		var magic = new string( reader.ReadChars( 4 ) );
		if ( magic != MAGIC )
			throw new InvalidDataException( $"Invalid index file: expected {MAGIC}, got {magic}" );
		
		// Read version
		index.Version = Read7BitInt( reader );
		
		// Read source files
		int sourceCount = Read7BitInt( reader );
		for ( int i = 0; i < sourceCount; i++ )
		{
			index.SourceFiles.Add( ReadString( reader ) );
		}
		
		// Read meshes
		int meshCount = Read7BitInt( reader );
		bool needsConversion = index.Version == 1; // Version 1 = raw USD, Version 2 = already s&box space
		
		for ( int i = 0; i < meshCount; i++ )
		{
			var mesh = new IndexedMeshRef();
			
			int sourceIdx = Read7BitInt( reader );
			mesh.SourcePath = sourceIdx < index.SourceFiles.Count ? index.SourceFiles[sourceIdx] : "";
			mesh.MeshName = ReadString( reader );
			mesh.MeshPath = ReadString( reader );
			
			// Position
			float px = reader.ReadSingle();
			float py = reader.ReadSingle();
			float pz = reader.ReadSingle();
			
			// Rotation (quaternion: x, y, z, w)
			float qx = reader.ReadSingle();
			float qy = reader.ReadSingle();
			float qz = reader.ReadSingle();
			float qw = reader.ReadSingle();
			
			// Scale
			float sx = reader.ReadSingle();
			float sy = reader.ReadSingle();
			float sz = reader.ReadSingle();
			
			if ( needsConversion )
			{
				// Version 1: Convert from USD to s&box coordinates
				// Position: USD (X, Y, Z) -> s&box (Y, -X, Z)
				mesh.Position = new Vector3( py, -px, pz );
				// Rotation: USD quaternion -> s&box quaternion
				mesh.Rotation = new Rotation( qy, -qx, qz, qw );
				// Scale: USD (X, Y, Z) -> s&box (Y, X, Z)
				mesh.Scale = new Vector3( sy, sx, sz );
			}
			else
			{
				// Version 2: Already in s&box space
				mesh.Position = new Vector3( px, py, pz );
				mesh.Rotation = new Rotation( qx, qy, qz, qw );
				mesh.Scale = new Vector3( sx, sy, sz );
			}
			
			// Flags
			byte flags = reader.ReadByte();
			mesh.HasSkeleton = (flags & 1) != 0;
			mesh.HasExtent = (flags & 2) != 0;
			
			// Extent (if present)
			if ( mesh.HasExtent )
			{
				float minX = reader.ReadSingle();
				float minY = reader.ReadSingle();
				float minZ = reader.ReadSingle();
				float maxX = reader.ReadSingle();
				float maxY = reader.ReadSingle();
				float maxZ = reader.ReadSingle();
				
				if ( needsConversion )
				{
					mesh.ExtentMin = new Vector3( minY, -minX, minZ );
					mesh.ExtentMax = new Vector3( maxY, -maxX, maxZ );
				}
				else
				{
					mesh.ExtentMin = new Vector3( minX, minY, minZ );
					mesh.ExtentMax = new Vector3( maxX, maxY, maxZ );
				}
			}
			
			index.Meshes.Add( mesh );
		}
		
		return index;
	}
	
	/// <summary>
	/// Group meshes by source file for efficient batch loading
	/// </summary>
	public Dictionary<string, List<IndexedMeshRef>> GroupBySourceFile()
	{
		var groups = new Dictionary<string, List<IndexedMeshRef>>();
		
		foreach ( var mesh in Meshes )
		{
			if ( !groups.TryGetValue( mesh.SourcePath, out var list ) )
			{
				list = new List<IndexedMeshRef>();
				groups[mesh.SourcePath] = list;
			}
			list.Add( mesh );
		}
		
		return groups;
	}
	
	/// <summary>
	/// Get unique geometry instances (for instancing support)
	/// Key: source_path|mesh_name, Value: list of transforms
	/// </summary>
	public Dictionary<string, List<Transform>> GetGeometryInstances()
	{
		var instances = new Dictionary<string, List<Transform>>();
		
		foreach ( var mesh in Meshes )
		{
			var key = $"{mesh.SourcePath}|{mesh.MeshName}";
			if ( !instances.TryGetValue( key, out var list ) )
			{
				list = new List<Transform>();
				instances[key] = list;
			}
			list.Add( mesh.WorldTransform );
		}
		
		return instances;
	}
	
	private static int Read7BitInt( BinaryReader reader )
	{
		int result = 0;
		int shift = 0;
		byte b;
		do
		{
			b = reader.ReadByte();
			result |= (b & 0x7F) << shift;
			shift += 7;
		} while ( (b & 0x80) != 0 );
		return result;
	}
	
	private static string ReadString( BinaryReader reader )
	{
		int length = Read7BitInt( reader );
		if ( length == 0 ) return "";
		var bytes = reader.ReadBytes( length );
		return System.Text.Encoding.UTF8.GetString( bytes );
	}
}

