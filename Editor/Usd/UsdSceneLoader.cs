using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox.Usd.Parser;
using Sandbox.Usd.Schema;

namespace Sandbox.Usd;

/// <summary>
/// Loads USD scene files and resolves the full scene hierarchy
/// </summary>
public class UsdSceneLoader
{
	private readonly string _basePath;
	private readonly HashSet<string> _loadedFiles = new();
	private readonly Dictionary<string, UsdStage> _stageCache = new();
	
	/// <summary>
	/// Skeletons found during scene loading, keyed by full path
	/// </summary>
	private readonly Dictionary<string, UsdSkeleton> _skeletonCache = new();
	
	/// <summary>
	/// Skip patterns for non-geometry files (audio, fx, etc.)
	/// Note: "_light" was too broad - matched "light" material variants, not light sources
	/// </summary>
	private static readonly string[] SKIP_PATTERNS = new[]
	{
		"/breadcrumbs/", "/endpoints/", "/audio/", "/lighting/",
		"/ui/", "/vfx/", "/fx/", "breadcrumb", "endpoint",
		"_audio", "_sound", "_fx", "_vfx",
		"_lighting"  // Matches mp_wz_island_lighting but NOT "light" material variants
	};
	
	/// <summary>
	/// Maximum depth for reference resolution
	/// </summary>
	public int MaxDepth { get; set; } = 32;
	
	/// <summary>
	/// Maximum number of files to process (0 = unlimited)
	/// </summary>
	public int MaxFiles { get; set; } = 0;
	
	/// <summary>
	/// Number of files to skip before starting to process (for building in ranges)
	/// </summary>
	public int SkipFiles { get; set; } = 0;
	
	/// <summary>
	/// Callback for progress updates
	/// </summary>
	public Action<string, int, int> OnProgress { get; set; }
	
	/// <summary>
	/// Callback to flush meshes periodically (for streaming). Called with current meshes, should return true to continue.
	/// After callback returns, LoadedMeshes will be cleared.
	/// </summary>
	public Func<List<(UsdMesh mesh, Transform worldTransform, string sourcePath)>, bool> OnFlushMeshes { get; set; }
	
	/// <summary>
	/// How often to call OnFlushMeshes (every N meshes, 0 = never auto-flush)
	/// </summary>
	public int FlushEveryNMeshes { get; set; } = 0;
	
	/// <summary>
	/// Total parse operations (may count same file multiple times if cache cleared)
	/// </summary>
	private int _totalParseOps = 0;
	
	/// <summary>
	/// Unique files loaded (never decreases, for progress tracking)
	/// </summary>
	private readonly HashSet<string> _uniqueFilesLoaded = new();
	
	/// <summary>
	/// Clear the stage cache to free memory (call between batches)
	/// </summary>
	public void ClearStageCache()
	{
		_stageCache.Clear();
	}
	
	/// <summary>
	/// Number of unique files processed so far (for progress %)
	/// </summary>
	public int FilesProcessed => _uniqueFilesLoaded.Count;
	
	/// <summary>
	/// All loaded prims with their world transforms
	/// </summary>
	public List<(UsdPrim prim, Transform worldTransform, string sourcePath)> LoadedPrims { get; } = new();
	
	/// <summary>
	/// All mesh prims with their world transforms
	/// </summary>
	public List<(UsdMesh mesh, Transform worldTransform, string sourcePath)> LoadedMeshes { get; } = new();
	
	/// <summary>
	/// Pending skeleton-bound meshes to resolve after all skeletons are loaded
	/// </summary>
	private readonly List<(UsdMesh mesh, UsdPrim prim, Transform worldTransform, string sourcePath)> _pendingSkinnedMeshes = new();
	
	public UsdSceneLoader( string basePath )
	{
		_basePath = basePath;
	}
	
	/// <summary>
	/// Load a scene file and resolve all references
	/// </summary>
	public void LoadScene( string relativePath )
	{
		var fullPath = ResolvePath( relativePath );
		if ( string.IsNullOrEmpty( fullPath ) || !File.Exists( fullPath ) )
		{
			Log.Warning( $"Scene file not found: {relativePath}" );
			return;
		}
		
		LoadedPrims.Clear();
		LoadedMeshes.Clear();
		_loadedFiles.Clear();
		_skeletonCache.Clear();
		_pendingSkinnedMeshes.Clear();
		
		var stage = LoadStage( fullPath );
		if ( stage == null )
			return;
		
		// Process sublayers first
		foreach ( var subLayer in stage.SubLayers )
		{
			var subLayerPath = ResolveRelativePath( fullPath, subLayer );
			if ( !string.IsNullOrEmpty( subLayerPath ) )
			{
				LoadSubLayer( subLayerPath, Transform.Zero, 0 );
			}
		}
		
		// Process root prims
		foreach ( var prim in stage.RootPrims )
		{
			ProcessPrim( prim, Transform.Zero, fullPath, 0 );
		}
		
		// Resolve skeleton bindings now that all skeletons are loaded
		ResolveSkinnedMeshes();
		
		Log.Info( $"Loaded scene with {LoadedPrims.Count} prims and {LoadedMeshes.Count} meshes" );
	}
	
	/// <summary>
	/// Resolve skeleton bindings for deferred skinned meshes
	/// </summary>
	private void ResolveSkinnedMeshes()
	{
		foreach ( var (mesh, prim, worldTransform, sourcePath) in _pendingSkinnedMeshes )
		{
			LoadedMeshes.Add( (mesh, worldTransform, sourcePath) );
		}
	}
	
	private void LoadSubLayer( string fullPath, Transform parentTransform, int depth )
	{
		if ( depth > MaxDepth || _loadedFiles.Contains( fullPath ) )
			return;
		
		if ( ShouldSkipPath( fullPath ) )
			return;
		
		_loadedFiles.Add( fullPath );
		
		var stage = LoadStage( fullPath );
		if ( stage == null )
			return;
		
		// Process sublayers recursively
		foreach ( var subLayer in stage.SubLayers )
		{
			var subLayerPath = ResolveRelativePath( fullPath, subLayer );
			if ( !string.IsNullOrEmpty( subLayerPath ) )
			{
				LoadSubLayer( subLayerPath, parentTransform, depth + 1 );
			}
		}
		
		// Process root prims
		foreach ( var prim in stage.RootPrims )
		{
			ProcessPrim( prim, parentTransform, fullPath, depth );
		}
	}
	

	private void ProcessPrim( UsdPrim prim, Transform parentTransform, string sourcePath, int depth, bool skipLocalTransform = false )
	{
		if ( depth > MaxDepth )
			return;
		
		// .geo.usda root prims have model origin offset, not scene placement - skip their local transform
		Transform worldTransform;
		if ( skipLocalTransform )
		{
			worldTransform = parentTransform;
		}
		else
		{
			var localTransform = prim.GetLocalTransform();
			worldTransform = parentTransform.ToWorld( localTransform );
		}
		
		
		// Process references
		foreach ( var reference in prim.References )
		{
			LoadReference( reference, worldTransform, sourcePath, depth + 1 );
		}
		
		// Process payloads
		foreach ( var payload in prim.Payloads )
		{
			LoadReference( payload, worldTransform, sourcePath, depth + 1 );
		}
		
		// Process variant sets - select the specified variant or default to first (usually "lod0")
		foreach ( var (variantSetName, variants) in prim.VariantSets )
		{
			if ( variants.Count == 0 )
				continue;
			
			// Get selected variant name (from prim.Variants) or default to first available
			var selectedVariant = prim.Variants.TryGetValue( variantSetName, out var sel ) 
				? sel 
				: variants.Keys.FirstOrDefault();
			
			// Process the selected variant's content
			if ( selectedVariant != null && variants.TryGetValue( selectedVariant, out var variantPrim ) )
			{
				// Variant prims may have payloads
				foreach ( var payload in variantPrim.Payloads )
				{
					LoadReference( payload, worldTransform, sourcePath, depth + 1 );
				}
				
				// Variant prims may have references too
				foreach ( var reference in variantPrim.References )
				{
					LoadReference( reference, worldTransform, sourcePath, depth + 1 );
				}
				
				// Process variant children
				foreach ( var child in variantPrim.Children )
				{
					ProcessPrim( child, worldTransform, sourcePath, depth );
				}
			}
		}
		
		// Add this prim
		LoadedPrims.Add( (prim, worldTransform, sourcePath) );
		
		// Cache Skeleton prims for skeleton binding lookup
		// Use sourcePath + prim.Path as key since multiple files have /rig/skel
		if ( prim.TypeName == "Skeleton" )
		{
			var skeleton = prim as UsdSkeleton ?? ConvertToSkeleton( prim );
			if ( skeleton != null )
			{
				var cacheKey = $"{sourcePath}|{prim.Path}";
				_skeletonCache[cacheKey] = skeleton;
			}
		}
		
		// If it's a mesh, add to mesh list (but respect SkipFiles)
		if ( prim.TypeName == "Mesh" && ShouldExtractFromFile( sourcePath ) )
		{
			var mesh = prim as UsdMesh ?? ConvertToMesh( prim );
			// Skip non-renderable meshes (guide, proxy) and meshes with no geometry
			if ( mesh != null && mesh.IsRenderable && mesh.Points.Count > 0 && mesh.FaceVertexIndices.Count > 0 )
			{
				// If mesh has skeleton binding, defer processing until all skeletons are loaded
				if ( mesh.HasSkeletonBinding )
				{
					_pendingSkinnedMeshes.Add( (mesh, prim, worldTransform, sourcePath) );
				}
				else
				{
					LoadedMeshes.Add( (mesh, worldTransform, sourcePath) );
					
					// Check if we should flush meshes to disk (streaming mode)
					if ( FlushEveryNMeshes > 0 && LoadedMeshes.Count >= FlushEveryNMeshes && OnFlushMeshes != null )
					{
						if ( !OnFlushMeshes( LoadedMeshes ) )
						{
							// Callback returned false, stop processing
							return;
						}
						LoadedMeshes.Clear();
						
						// Aggressively clear the stage cache - this is the main memory hog
						// Keep only 20 most recent files (we'll re-parse if needed)
						if ( _stageCache.Count > 100 )
						{
							var keysToRemove = _stageCache.Keys.Take( _stageCache.Count - 20 ).ToList();
							foreach ( var key in keysToRemove )
								_stageCache.Remove( key );
						}
						
						// Also clear the loaded prims list
						LoadedPrims.Clear();
					}
				}
			}
		}
		
		// Process children
		foreach ( var child in prim.Children )
		{
			ProcessPrim( child, worldTransform, sourcePath, depth );
		}
	}
	
	private void LoadReference( string referencePath, Transform parentTransform, string sourceFile, int depth )
	{
		if ( depth > MaxDepth )
			return;
		
		if ( ShouldSkipPath( referencePath ) )
			return;
		
		// Parse the reference path - it may have a prim path suffix like @file.usd@</path/to/prim>
		var primPath = "";
		var filePath = referencePath;
		
		int primPathStart = referencePath.IndexOf( '<' );
		if ( primPathStart >= 0 )
		{
			filePath = referencePath.Substring( 0, primPathStart ).Trim();
			int primPathEnd = referencePath.IndexOf( '>', primPathStart );
			if ( primPathEnd > primPathStart )
			{
				primPath = referencePath.Substring( primPathStart + 1, primPathEnd - primPathStart - 1 );
			}
		}
		
		// Handle .usd -> .usda conversion (files may have been converted)
		if ( filePath.EndsWith( ".usd" ) )
		{
			filePath = filePath.Substring( 0, filePath.Length - 4 ) + ".usda";
		}
		
		var fullPath = ResolveRelativePath( sourceFile, filePath );
		if ( string.IsNullOrEmpty( fullPath ) || !File.Exists( fullPath ) )
		{
			// Try without .usda extension change
			fullPath = ResolveRelativePath( sourceFile, referencePath.Split( '<' )[0].Trim() );
			if ( string.IsNullOrEmpty( fullPath ) || !File.Exists( fullPath ) )
				return;
		}
		
		var stage = LoadStage( fullPath );
		if ( stage == null )
			return;
		
		// Find the referenced prim
		IEnumerable<UsdPrim> primsToProcess;
		if ( !string.IsNullOrEmpty( primPath ) )
		{
			var targetPrim = stage.GetPrimAtPath( primPath );
			primsToProcess = targetPrim != null ? new[] { targetPrim } : Array.Empty<UsdPrim>();
		}
		else
		{
			primsToProcess = stage.RootPrims;
		}
		
		// .geo.usda files have origin offsets baked in - skip when loaded via .gdt.usda wrappers
		bool skipRootTransform = fullPath.EndsWith( ".geo.usda", StringComparison.OrdinalIgnoreCase );
		
		foreach ( var prim in primsToProcess )
		{
			ProcessPrim( prim, parentTransform, fullPath, depth, skipRootTransform );
		}
	}
	
	private UsdStage LoadStage( string fullPath )
	{
		if ( _stageCache.TryGetValue( fullPath, out var cached ) )
			return cached;
		
		if ( !File.Exists( fullPath ) )
			return null;
		
		// Track this as a seen file
		bool isNewFile = _uniqueFilesLoaded.Add( fullPath );
		
		// Check max files limit (files processed AFTER skipping)
		int filesAfterSkip = _uniqueFilesLoaded.Count - SkipFiles;
		if ( MaxFiles > 0 && filesAfterSkip > MaxFiles )
			return null;
		
		try
		{
			_totalParseOps++;
			if ( isNewFile )
				OnProgress?.Invoke( Path.GetFileName( fullPath ), _uniqueFilesLoaded.Count, SkipFiles + (MaxFiles > 0 ? MaxFiles : -1) );
			var stage = UsdaParser.ParseFile( fullPath );
			_stageCache[fullPath] = stage;
			return stage;
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Failed to parse USD file {fullPath}: {ex.Message}" );
			return null;
		}
	}
	
	/// <summary>
	/// Check if we should extract meshes from this file (respects SkipFiles)
	/// </summary>
	public bool ShouldExtractFromFile( string fullPath )
	{
		if ( SkipFiles <= 0 )
			return true;
		
		// Find the index of this file in the order we discovered it
		// We need to track discovery order, not just unique files
		// For now, use a simple approach: count how many files we've seen
		int fileIndex = 0;
		foreach ( var f in _uniqueFilesLoaded )
		{
			fileIndex++;
			if ( f == fullPath )
				break;
		}
		
		return fileIndex > SkipFiles;
	}
	
	private bool ShouldSkipPath( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return true;
		
		var lowerPath = path.ToLowerInvariant();
		foreach ( var pattern in SKIP_PATTERNS )
		{
			if ( lowerPath.Contains( pattern ) )
				return true;
		}
		return false;
	}
	
	private string ResolvePath( string relativePath )
	{
		if ( Path.IsPathRooted( relativePath ) )
			return relativePath;
		
		return Path.Combine( _basePath, relativePath.Replace( '/', '\\' ) );
	}
	
	private string ResolveRelativePath( string sourceFile, string relativePath )
	{
		if ( string.IsNullOrEmpty( relativePath ) )
			return null;
		
		// Remove ./ prefix
		if ( relativePath.StartsWith( "./" ) )
			relativePath = relativePath.Substring( 2 );
		
		var sourceDir = Path.GetDirectoryName( sourceFile );
		var fullPath = Path.GetFullPath( Path.Combine( sourceDir, relativePath.Replace( '/', '\\' ) ) );
		
		return fullPath;
	}
	
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
		
		// Copy relationships for skeleton binding
		foreach ( var rel in prim.Relationships )
		{
			mesh.Relationships.Add( rel );
		}
		
		return mesh;
	}
	
	private static UsdSkeleton ConvertToSkeleton( UsdPrim prim )
	{
		var skeleton = new UsdSkeleton
		{
			Name = prim.Name,
			Path = prim.Path,
			TypeName = "Skeleton",
			Specifier = prim.Specifier,
			Parent = prim.Parent
		};
		
		foreach ( var attr in prim.Attributes )
		{
			skeleton.Attributes[attr.Key] = attr.Value;
		}
		
		foreach ( var meta in prim.Metadata )
		{
			skeleton.Metadata[meta.Key] = meta.Value;
		}
		
		return skeleton;
	}
	
	/// <summary>
	/// Get the bind transform for a skinned mesh from its skeleton
	/// </summary>
	private Transform? GetSkeletonBindTransform( UsdMesh mesh, UsdPrim prim, string sourcePath )
	{
		var skelPath = mesh.SkeletonPath;
		if ( string.IsNullOrEmpty( skelPath ) )
			return null;
		
		// Try to find skeleton in cache using sourcePath + relative skeleton path
		// The skeleton is in the same file as the mesh, so use that file's path
		UsdSkeleton skeleton = null;
		
		// Extract the relative skeleton path (e.g., /rig/skel from /model_lod0/rig/skel)
		var rigSkelIdx = skelPath.IndexOf( "/rig/skel" );
		if ( rigSkelIdx >= 0 )
		{
			var relativeSkelPath = skelPath.Substring( rigSkelIdx );
			var cacheKey = $"{sourcePath}|{relativeSkelPath}";
			_skeletonCache.TryGetValue( cacheKey, out skeleton );
		}
		
		if ( skeleton == null )
			return null;
		
		var bindTransforms = skeleton.BindTransforms;
		var jointIndices = mesh.JointIndices;
		
		if ( bindTransforms.Count == 0 || jointIndices.Count == 0 )
			return null;
		
		// For meshes where all vertices are bound to the same joint (weight = 1.0),
		// we just need to apply that joint's bind transform
		var firstJointIndex = jointIndices[0];
		
		// For static meshes, use the first joint's bind transform even if multi-joint
		// This handles the common case where skeleton binding is used for instancing/positioning
		if ( firstJointIndex < 0 || firstJointIndex >= bindTransforms.Count )
			return null;
		
		// Get the bind transform matrix
		var bindMatrix = bindTransforms[firstJointIndex];
		
		// Convert matrix to Transform using coordinate system conversion
		return MatrixToTransform( bindMatrix );
	}
	
	private static Transform MatrixToTransform( Matrix matrix )
	{
		// Extract translation from matrix (row 4)
		var usdPos = new Vector3( matrix.M41, matrix.M42, matrix.M43 );
		
		// Apply coordinate system conversion: USD (x,y,z) -> s&box (y,-x,z)
		var position = new Vector3( usdPos.y, -usdPos.x, usdPos.z );
		
		// Extract scale from matrix (length of each row's first 3 components)
		var usdScaleX = new Vector3( matrix.M11, matrix.M12, matrix.M13 ).Length;
		var usdScaleY = new Vector3( matrix.M21, matrix.M22, matrix.M23 ).Length;
		var usdScaleZ = new Vector3( matrix.M31, matrix.M32, matrix.M33 ).Length;
		var scale = new Vector3( usdScaleY, usdScaleX, usdScaleZ );
		
		// Extract rotation by normalizing the matrix
		var forward = new Vector3( matrix.M21 / usdScaleY, matrix.M22 / usdScaleY, matrix.M23 / usdScaleY );
		var up = new Vector3( matrix.M31 / usdScaleZ, matrix.M32 / usdScaleZ, matrix.M33 / usdScaleZ );
		
		// Convert forward/up with coordinate system transformation
		var sboxForward = new Vector3( forward.y, -forward.x, forward.z );
		var sboxUp = new Vector3( up.y, -up.x, up.z );
		
		var rotation = Rotation.LookAt( sboxForward, sboxUp );
		
		return new Transform( position, rotation, scale );
	}
	
	private static UsdPrim FindAncestorOfType( UsdPrim prim, string typeName )
	{
		var current = prim.Parent;
		while ( current != null )
		{
			if ( current.TypeName == typeName )
				return current;
			current = current.Parent;
		}
		return null;
	}
	
	private static UsdSkeleton FindSkeletonInPrim( UsdPrim root, string skelPath )
	{
		// Remove leading / from path
		var pathParts = skelPath.TrimStart( '/' ).Split( '/' );
		
		// Navigate through the prim tree to find the skeleton
		var current = root;
		foreach ( var part in pathParts )
		{
			if ( string.IsNullOrEmpty( part ) )
				continue;
			
			var child = current.Children.FirstOrDefault( c => c.Name == part );
			if ( child == null )
				return null;
			current = child;
		}
		
		if ( current?.TypeName == "Skeleton" )
		{
			return current as UsdSkeleton ?? ConvertToSkeleton( current );
		}
		
		return null;
	}
}
