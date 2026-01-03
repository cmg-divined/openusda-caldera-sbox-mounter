using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Editor;
using Sandbox.Usd;
using Sandbox.Usd.Parser;

namespace Sandbox;

/// <summary>
/// Editor menu for USD loading and testing
/// </summary>
public static class UsdEditorMenu
{
	// Cache for built models to avoid rebuilding same geometry
	private static readonly Dictionary<string, Model> _modelCache = new();
	
	[Menu( "Editor", "USD/Clear Model Cache" )]
	public static void ClearModelCache()
	{
		var count = _modelCache.Count;
		_modelCache.Clear();
		Log.Info( $"Cleared {count} cached models" );
	}
	
	[Menu( "Editor", "USD/Load Hotel_01 (True GPU Batch)" )]
	public static async void LoadHotelBatch()
	{
		var indexPath = Path.Combine( UsdMount.BasePath, @"map_source\prefabs\br\wz_vg\mp_wz_island\commercial\hotel_01.usdi" );
		
		if ( !File.Exists( indexPath ) )
		{
			Log.Warning( $"Index file not found: {indexPath}" );
			Log.Info( "Use 'Build Hotel_01 Index' menu option first." );
			return;
		}
		
		await LoadFromIndexBatchAsync( indexPath, int.MaxValue );
	}
	
	[Menu( "Editor", "USD/Load Caldera (SceneObjects FULL)" )]
	public static async void LoadCalderaSceneObjectsFull()
	{
		var indexPath = Path.Combine( UsdMount.BasePath, "caldera.usdi" );
		
		if ( !File.Exists( indexPath ) )
		{
			Log.Warning( $"Index file not found: {indexPath}" );
			Log.Info( "Use 'Build Caldera Index' menu option first." );
			return;
		}
		
		await LoadFromIndexInstancedAsync( indexPath, int.MaxValue );
	}
	
	[Menu( "Editor", "USD/Load Caldera (SceneObjects 50%)" )]
	public static async void LoadCalderaSceneObjects50()
	{
		var indexPath = Path.Combine( UsdMount.BasePath, "caldera.usdi" );
		
		if ( !File.Exists( indexPath ) )
		{
			Log.Warning( $"Index file not found: {indexPath}" );
			Log.Info( "Use 'Build Caldera Index' menu option first." );
			return;
		}
		
		// 50% of ~640k meshes = ~320k meshes
		await LoadFromIndexInstancedAsync( indexPath, 320000 );
	}
	
	[Menu( "Editor", "USD/Build Hotel_01 Index" )]
	public static void BuildHotelIndex()
	{
		BuildIndexInternal( 
			@"map_source\prefabs\br\wz_vg\mp_wz_island\commercial\hotel_01.usda",
			Path.Combine( UsdMount.BasePath, @"map_source\prefabs\br\wz_vg\mp_wz_island\commercial\hotel_01.usdi" ),
			"Hotel_01"
		);
	}
	
	[Menu( "Editor", "USD/Build Caldera Index" )]
	public static void BuildCalderaIndexFull() => BuildIndexStreaming( "caldera.usda", "caldera.usdi", "Caldera FULL", 0, 0 );
	
	/// <summary>
	/// Builds index by streaming meshes to disk during traversal to keep memory usage low
	/// </summary>
	private static void BuildIndexStreaming( string inputRelPath, string outputFileName, string sceneName, int maxFiles, int skipFiles = 0 )
	{
		const int FLUSH_EVERY_N_MESHES = 10000; // Flush to disk every 10k meshes
		
		var stopwatch = Stopwatch.StartNew();
		var outputPath = Path.Combine( UsdMount.BasePath, outputFileName );
		
		Log.Info( $"=== Building {sceneName} Index (Streaming) ===" );
		Log.Info( $"Input: {inputRelPath}" );
		Log.Info( $"Output: {outputPath}" );
		Log.Info( $"File range: {skipFiles} to {(maxFiles > 0 ? (skipFiles + maxFiles).ToString() : "END")}" );
		Log.Info( $"Flush every: {FLUSH_EVERY_N_MESHES} meshes" );
		
		// Count total USD files for progress percentage (entire USD directory, both .usda and .usd)
		Log.Info( "Counting source files..." );
		int totalSourceFiles = 0;
		if ( Directory.Exists( UsdMount.BasePath ) )
		{
			totalSourceFiles = Directory.EnumerateFiles( UsdMount.BasePath, "*.usda", SearchOption.AllDirectories ).Count()
			                 + Directory.EnumerateFiles( UsdMount.BasePath, "*.usd", SearchOption.AllDirectories ).Count();
		}
		Log.Info( $"Total source files: {totalSourceFiles}" );
		
		// Create temp directory for batch files
		var tempDir = Path.Combine( Path.GetDirectoryName( outputPath ), "index_temp" );
		if ( Directory.Exists( tempDir ) )
			Directory.Delete( tempDir, true );
		Directory.CreateDirectory( tempDir );
		
		var tempFiles = new List<string>();
		var allSourceFiles = new HashSet<string>();
		int totalMeshes = 0;
		int batchNumber = 0;
		var lastLogTime = Stopwatch.StartNew();
		
		// Force initial GC
		GC.Collect( GC.MaxGeneration, GCCollectionMode.Forced, true, true );
		GC.WaitForPendingFinalizers();
		
		var loader = new UsdSceneLoader( UsdMount.BasePath );
		loader.MaxFiles = maxFiles;
		loader.SkipFiles = skipFiles;
		loader.FlushEveryNMeshes = FLUSH_EVERY_N_MESHES;
		
		// Set up flush callback - writes batch to disk and clears memory
		loader.OnFlushMeshes = ( meshes ) =>
		{
			batchNumber++;
			var tempFile = Path.Combine( tempDir, $"batch_{batchNumber:D4}.bin" );
			tempFiles.Add( tempFile );
			
			WriteBatchFile( tempFile, meshes );
			
			foreach ( var m in meshes )
				allSourceFiles.Add( m.sourcePath );
			
			totalMeshes += meshes.Count;
			
			var mem = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
			var pct = totalSourceFiles > 0 ? (100.0 * loader.FilesProcessed / totalSourceFiles) : 0;
			Log.Info( $"  FLUSH batch {batchNumber}: {pct:F1}% | {meshes.Count} meshes ({totalMeshes} total) | {loader.FilesProcessed}/{totalSourceFiles} files | {mem}MB RAM" );
			
			// Force GC after writing to disk
			GC.Collect( GC.MaxGeneration, GCCollectionMode.Forced, true, true );
			GC.WaitForPendingFinalizers();
			
			return true; // Continue processing
		};
		
		loader.OnProgress = ( file, current, total ) =>
		{
			if ( lastLogTime.ElapsedMilliseconds > 5000 || current % 2000 == 0 )
			{
				var elapsed = stopwatch.Elapsed.TotalSeconds;
				var rate = current / Math.Max( elapsed, 0.1 );
				var mem = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
				var pct = totalSourceFiles > 0 ? (100.0 * current / totalSourceFiles) : 0;
				var eta = rate > 0 && totalSourceFiles > 0 ? (totalSourceFiles - current) / rate : 0;
				Log.Info( $"  {pct:F1}% | {current}/{totalSourceFiles} files | {totalMeshes + loader.LoadedMeshes.Count} meshes | {rate:F0}/sec | ETA {eta:F0}s | {mem}MB | {file}" );
				lastLogTime.Restart();
			}
		};
		
		try
		{
			loader.LoadScene( inputRelPath );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to load scene: {ex.Message}" );
			return;
		}
		
		// Flush any remaining meshes
		if ( loader.LoadedMeshes.Count > 0 )
		{
			batchNumber++;
			var tempFile = Path.Combine( tempDir, $"batch_{batchNumber:D4}.bin" );
			tempFiles.Add( tempFile );
			WriteBatchFile( tempFile, loader.LoadedMeshes );
			
			foreach ( var m in loader.LoadedMeshes )
				allSourceFiles.Add( m.sourcePath );
			
			totalMeshes += loader.LoadedMeshes.Count;
			Log.Info( $"  FLUSH final batch {batchNumber}: {loader.LoadedMeshes.Count} meshes" );
		}
		
		int filesProcessed = loader.FilesProcessed;
		
		// Clean up loader
		loader.LoadedMeshes.Clear();
		loader.LoadedPrims.Clear();
		loader.ClearStageCache();
		loader = null;
		
		GC.Collect( GC.MaxGeneration, GCCollectionMode.Forced, true, true );
		GC.WaitForPendingFinalizers();
		
		var loadTime = stopwatch.Elapsed.TotalSeconds;
		Log.Info( $"Scene traversal complete: {filesProcessed} files, {totalMeshes} meshes in {loadTime:F1}s" );
		Log.Info( $"Combining {tempFiles.Count} batch files..." );
		
		// Combine all temp files into final index
		try
		{
			CombineBatchFiles( outputPath, tempFiles, allSourceFiles.OrderBy( s => s ).ToList() );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to combine batch files: {ex.Message}" );
			return;
		}
		
		// Cleanup temp directory
		try
		{
			Directory.Delete( tempDir, true );
		}
		catch { }
		
		stopwatch.Stop();
		var fileSize = new FileInfo( outputPath ).Length / 1024.0 / 1024.0;
		Log.Info( $"=== {sceneName} Index Built ===" );
		Log.Info( $"  Total time: {stopwatch.Elapsed.TotalSeconds:F1}s" );
		Log.Info( $"  Files: {filesProcessed}" );
		Log.Info( $"  Meshes: {totalMeshes}" );
		Log.Info( $"  File size: {fileSize:F2} MB" );
		Log.Info( $"  Output: {outputPath}" );
	}
	
	/// <summary>
	/// Write a batch of meshes to a temporary binary file
	/// Uses Int32 length prefix for strings (matches ReadBatchString)
	/// </summary>
	private static void WriteBatchFile( string path, List<(Usd.Schema.UsdMesh mesh, Transform worldTransform, string sourcePath)> meshes )
	{
		using var stream = new FileStream( path, FileMode.Create, FileAccess.Write, FileShare.None, 65536 );
		using var writer = new BinaryWriter( stream );
		
		writer.Write( meshes.Count );
		
		foreach ( var (mesh, transform, sourcePath) in meshes )
		{
			// Use Int32 length prefix (matches ReadBatchString)
			WriteBatchString( writer, sourcePath );
			WriteBatchString( writer, mesh.Name );
			WriteBatchString( writer, mesh.Path );
			
			// Position
			writer.Write( transform.Position.x );
			writer.Write( transform.Position.y );
			writer.Write( transform.Position.z );
			
			// Rotation
			writer.Write( transform.Rotation.x );
			writer.Write( transform.Rotation.y );
			writer.Write( transform.Rotation.z );
			writer.Write( transform.Rotation.w );
			
			// Scale
			writer.Write( transform.Scale.x );
			writer.Write( transform.Scale.y );
			writer.Write( transform.Scale.z );
			
			// Extent
			var extent = mesh.Extent;
			writer.Write( extent.HasValue );
			if ( extent.HasValue )
			{
				writer.Write( extent.Value.Min.y );
				writer.Write( -extent.Value.Min.x );
				writer.Write( extent.Value.Min.z );
				writer.Write( extent.Value.Max.y );
				writer.Write( -extent.Value.Max.x );
				writer.Write( extent.Value.Max.z );
			}
		}
	}
	
	private static void WriteBatchString( BinaryWriter writer, string s )
	{
		var bytes = System.Text.Encoding.UTF8.GetBytes( s ?? "" );
		writer.Write( bytes.Length ); // Int32 length
		writer.Write( bytes );
	}
	
	/// <summary>
	/// Combine batch files into final index
	/// </summary>
	private static void CombineBatchFiles( string outputPath, List<string> batchFiles, List<string> sourceFiles )
	{
		// Build source file index
		var sourceIndex = sourceFiles.Select( (s, i) => (s, i) ).ToDictionary( x => x.s, x => x.i );
		
		using var outStream = new FileStream( outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536 );
		using var writer = new BinaryWriter( outStream );
		
		// Magic header
		writer.Write( "USDI".ToCharArray() );
		
		// Version
		Write7BitInt( writer, 2 );
		
		// Write source files
		Write7BitInt( writer, sourceFiles.Count );
		foreach ( var source in sourceFiles )
		{
			WriteString( writer, source );
		}
		
		// Count total meshes first
		int totalMeshes = 0;
		foreach ( var batchFile in batchFiles )
		{
			using var fs = new FileStream( batchFile, FileMode.Open, FileAccess.Read );
			using var br = new BinaryReader( fs );
			totalMeshes += br.ReadInt32();
		}
		
		Write7BitInt( writer, totalMeshes );
		
		// Now write all mesh data
		foreach ( var batchFile in batchFiles )
		{
			using var fs = new FileStream( batchFile, FileMode.Open, FileAccess.Read );
			using var br = new BinaryReader( fs );
			
			int count = br.ReadInt32();
			for ( int i = 0; i < count; i++ )
			{
				var sourcePath = ReadBatchString( br );
				var meshName = ReadBatchString( br );
				var meshPath = ReadBatchString( br );
				
				float px = br.ReadSingle();
				float py = br.ReadSingle();
				float pz = br.ReadSingle();
				float qx = br.ReadSingle();
				float qy = br.ReadSingle();
				float qz = br.ReadSingle();
				float qw = br.ReadSingle();
				float sx = br.ReadSingle();
				float sy = br.ReadSingle();
				float sz = br.ReadSingle();
				
				bool hasExtent = br.ReadBoolean();
				float minX = 0, minY = 0, minZ = 0, maxX = 0, maxY = 0, maxZ = 0;
				if ( hasExtent )
				{
					minX = br.ReadSingle();
					minY = br.ReadSingle();
					minZ = br.ReadSingle();
					maxX = br.ReadSingle();
					maxY = br.ReadSingle();
					maxZ = br.ReadSingle();
				}
				
				// Write to final index
				Write7BitInt( writer, sourceIndex.TryGetValue( sourcePath, out var idx ) ? idx : 0 );
				WriteString( writer, meshName );
				WriteString( writer, meshPath );
				
				writer.Write( px );
				writer.Write( py );
				writer.Write( pz );
				writer.Write( qx );
				writer.Write( qy );
				writer.Write( qz );
				writer.Write( qw );
				writer.Write( sx );
				writer.Write( sy );
				writer.Write( sz );
				
				byte flags = hasExtent ? (byte)2 : (byte)0;
				writer.Write( flags );
				
				if ( hasExtent )
				{
					writer.Write( minX );
					writer.Write( minY );
					writer.Write( minZ );
					writer.Write( maxX );
					writer.Write( maxY );
					writer.Write( maxZ );
				}
			}
		}
		
		Log.Info( $"Combined {batchFiles.Count} batches into {totalMeshes} total meshes" );
	}
	
	private static string ReadBatchString( BinaryReader reader )
	{
		int len = reader.ReadInt32();
		if ( len == 0 ) return "";
		var bytes = reader.ReadBytes( len );
		return System.Text.Encoding.UTF8.GetString( bytes );
	}
	
	private static void BuildIndexInternal( string inputPath, string outputPath, string sceneName )
	{
		var stopwatch = Stopwatch.StartNew();
		
		Log.Info( $"=== Building {sceneName} Index ===" );
		Log.Info( $"Input: {inputPath}" );
		Log.Info( $"Output: {outputPath}" );
		Log.Info( "This may take several minutes for large scenes..." );
		
		// Force GC before starting to free up memory
		GC.Collect();
		GC.WaitForPendingFinalizers();
		
		int filesProcessed = 0;
		var lastLogTime = Stopwatch.StartNew();
		
		// Use the working C# loader
		var loader = new UsdSceneLoader( UsdMount.BasePath );
		loader.OnProgress = ( file, current, total ) =>
		{
			filesProcessed = current;
			// Log every 5 seconds or every 500 files
			if ( lastLogTime.ElapsedMilliseconds > 5000 || current % 500 == 0 )
			{
				var elapsed = stopwatch.Elapsed.TotalSeconds;
				var rate = current / Math.Max( elapsed, 0.1 );
				Log.Info( $"  Progress: {current} files | {rate:F1} files/sec | {elapsed:F0}s elapsed" );
				lastLogTime.Restart();
			}
		};
		
		try
		{
			loader.LoadScene( inputPath );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to load scene: {ex.Message}" );
			return;
		}
		
		var loadTime = stopwatch.Elapsed.TotalSeconds;
		Log.Info( $"Loaded {loader.LoadedMeshes.Count} meshes from {filesProcessed} files in {loadTime:F1}s" );
		
		// Write the index file
		Log.Info( "Writing index file..." );
		try
		{
			WriteIndexFile( outputPath, loader.LoadedMeshes );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to write index: {ex.Message}" );
			return;
		}
		
		// Clean up to free memory
		loader.LoadedMeshes.Clear();
		loader.LoadedPrims.Clear();
		GC.Collect();
		
		stopwatch.Stop();
		Log.Info( $"=== {sceneName} Index Built ===" );
		Log.Info( $"  Total time: {stopwatch.Elapsed.TotalSeconds:F1}s" );
		Log.Info( $"  Output: {outputPath}" );
	}
	
	private static void WriteIndexFile( string outputPath, List<(Usd.Schema.UsdMesh mesh, Transform worldTransform, string sourcePath)> meshes )
	{
		// Group by source file to build the source file list
		var sourceFiles = meshes.Select( m => m.sourcePath ).Distinct().OrderBy( s => s ).ToList();
		var sourceIndex = sourceFiles.Select( (s, i) => (s, i) ).ToDictionary( x => x.s, x => x.i );
		
		using var stream = new FileStream( outputPath, FileMode.Create, FileAccess.Write );
		using var writer = new BinaryWriter( stream );
		
		// Magic header
		writer.Write( "USDI".ToCharArray() );
		
		// Version
		Write7BitInt( writer, 2 ); // Version 2 = transforms already in s&box space
		
		// Write source files
		Write7BitInt( writer, sourceFiles.Count );
		foreach ( var source in sourceFiles )
		{
			WriteString( writer, source );
		}
		
		// Write meshes
		Write7BitInt( writer, meshes.Count );
		foreach ( var (mesh, transform, sourcePath) in meshes )
		{
			Write7BitInt( writer, sourceIndex[sourcePath] );
			WriteString( writer, mesh.Name );
			WriteString( writer, mesh.Path );
			
			// Position - already in s&box space from the loader
			writer.Write( transform.Position.x );
			writer.Write( transform.Position.y );
			writer.Write( transform.Position.z );
			
			// Rotation as quaternion - already in s&box space
			writer.Write( transform.Rotation.x );
			writer.Write( transform.Rotation.y );
			writer.Write( transform.Rotation.z );
			writer.Write( transform.Rotation.w );
			
			// Scale - already in s&box space
			writer.Write( transform.Scale.x );
			writer.Write( transform.Scale.y );
			writer.Write( transform.Scale.z );
			
			// Flags
			byte flags = 0;
			// TODO: detect skeleton binding
			var extent = mesh.Extent;
			if ( extent.HasValue ) flags |= 2;
			writer.Write( flags );
			
			// Extent (if present) - convert to s&box space
			if ( extent.HasValue )
			{
				// USD (x, y, z) -> s&box (y, -x, z)
				writer.Write( extent.Value.Min.y );
				writer.Write( -extent.Value.Min.x );
				writer.Write( extent.Value.Min.z );
				writer.Write( extent.Value.Max.y );
				writer.Write( -extent.Value.Max.x );
				writer.Write( extent.Value.Max.z );
			}
		}
		
		Log.Info( $"Wrote {meshes.Count} meshes from {sourceFiles.Count} source files" );
	}
	
	private static void Write7BitInt( BinaryWriter writer, int value )
	{
		uint v = (uint)value;
		while ( v >= 0x80 )
		{
			writer.Write( (byte)((v & 0x7F) | 0x80) );
			v >>= 7;
		}
		writer.Write( (byte)v );
	}
	
	private static void WriteString( BinaryWriter writer, string s )
	{
		var bytes = System.Text.Encoding.UTF8.GetBytes( s );
		Write7BitInt( writer, bytes.Length );
		writer.Write( bytes );
	}
	
	/// <summary>
	/// Load scene using GPU instancing - way faster for huge scenes, no GameObjects per instance
	/// </summary>
	private static async Task LoadFromIndexInstancedAsync( string indexPath, int maxMeshes )
	{
		var stopwatch = Stopwatch.StartNew();
		
		Log.Info( $"[INSTANCED] Loading index from: {indexPath}" );
		
		// Load the index file (fast - binary format)
		UsdSceneIndex index;
		try
		{
			index = UsdSceneIndex.Load( indexPath );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to load index: {ex.Message}" );
			return;
		}
		
		Log.Info( $"Index loaded in {stopwatch.ElapsedMilliseconds}ms: {index.Meshes.Count} meshes from {index.SourceFiles.Count} source files" );
		
		// USD uses Z-up, s&box uses Z-up too but with different scale
		float scale = 0.0254f * 39.3701f;
		
		// Group meshes by source file
		var meshesBySourceFile = new Dictionary<string, List<(IndexedMeshRef mesh, Transform scaledTransform)>>();
		
		int totalMeshes = 0;
		foreach ( var mesh in index.Meshes )
		{
			if ( totalMeshes >= maxMeshes )
				break;
			
			if ( !meshesBySourceFile.TryGetValue( mesh.SourcePath, out var list ) )
			{
				list = new List<(IndexedMeshRef, Transform)>();
				meshesBySourceFile[mesh.SourcePath] = list;
			}
			
			var scaledTransform = new Transform(
				mesh.Position * scale,
				mesh.Rotation,
				mesh.Scale * scale
			);
			
			list.Add( (mesh, scaledTransform) );
			totalMeshes++;
		}
		
		Log.Info( $"[INSTANCED] {totalMeshes} instances across {meshesBySourceFile.Count} source files" );
		
		// Get scene world from the editor session (not Game.ActiveScene which may be different)
		using var sceneScope = SceneEditorSession.Scope();
		var editorSession = SceneEditorSession.Active;
		if ( editorSession?.Scene == null )
		{
			Log.Error( "No active editor scene!" );
			return;
		}
		
		var sceneWorld = editorSession.Scene.SceneWorld;
		Log.Info( $"[INSTANCED] Using editor scene: {editorSession.Scene.Name}, SceneWorld valid={sceneWorld?.IsValid()}" );
		
		// Create instanced renderer
		var instancedRenderer = new Usd.UsdInstancedSceneRenderer( sceneWorld );
		
		// Model cache
		var modelCache = new Dictionary<string, Model>();
		int filesProcessed = 0;
		int modelsCreated = 0;
		int instancesAdded = 0;
		int failed = 0;
		
		const int PARSE_BATCH_SIZE = 2000;
		const int LOG_INTERVAL = 100;
		var sourceFiles = meshesBySourceFile.Keys.ToList();
		int totalFiles = sourceFiles.Count;
		
		Log.Info( $"[INSTANCED] Processing {totalFiles} geometry files..." );
		
		for ( int batchStart = 0; batchStart < totalFiles; batchStart += PARSE_BATCH_SIZE )
		{
			int batchEnd = Math.Min( batchStart + PARSE_BATCH_SIZE, totalFiles );
			var batchFiles = sourceFiles.Skip( batchStart ).Take( batchEnd - batchStart ).ToList();
			
			// Parse batch in parallel
			var batchStages = new ConcurrentDictionary<string, Usd.Schema.UsdStage>();
			await Task.Run( () =>
			{
				Parallel.ForEach( batchFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, sourceFile =>
				{
					try
					{
						if ( File.Exists( sourceFile ) )
						{
							var stage = UsdaParser.ParseFile( sourceFile );
							batchStages[sourceFile] = stage;
						}
					}
					catch
					{
						System.Threading.Interlocked.Increment( ref failed );
					}
				} );
			} );
			
			// Build models and add instances
			foreach ( var sourceFile in batchFiles )
			{
				filesProcessed++;
				
				if ( !batchStages.TryGetValue( sourceFile, out var stage ) )
					continue;
				
				var meshInstances = meshesBySourceFile[sourceFile];
				var missingModels = new HashSet<string>();
				
				foreach ( var (meshRef, _) in meshInstances )
				{
					var cacheKey = $"{sourceFile}|{meshRef.MeshName}";
					if ( !modelCache.ContainsKey( cacheKey ) )
						missingModels.Add( meshRef.MeshName );
				}
				
				if ( missingModels.Count > 0 )
				{
					foreach ( var prim in stage.Traverse() )
					{
						if ( prim.TypeName != "Mesh" )
							continue;
						
						if ( !missingModels.Contains( prim.Name ) )
							continue;
						
						var usdMesh = prim as Usd.Schema.UsdMesh ?? ConvertToMesh( prim );
						if ( usdMesh == null )
							continue;
						
						var model = BuildModelFromMesh( usdMesh );
						if ( model != null )
						{
							var cacheKey = $"{sourceFile}|{prim.Name}";
							modelCache[cacheKey] = model;
							modelsCreated++;
						}
					}
				}
				
				// Create SceneObjects IMMEDIATELY (not deferred) for progressive loading
				foreach ( var (meshRef, transform) in meshInstances )
				{
					var cacheKey = $"{sourceFile}|{meshRef.MeshName}";
					if ( !modelCache.TryGetValue( cacheKey, out var model ) )
						continue;
					
					// Create SceneObject immediately - objects appear as they load
					instancedRenderer.AddInstanceImmediate( model, transform );
					instancesAdded++;
				}
				
				if ( filesProcessed % LOG_INTERVAL == 0 )
				{
					double elapsed = stopwatch.Elapsed.TotalSeconds;
					double rate = filesProcessed / elapsed;
					double remaining = (totalFiles - filesProcessed) / Math.Max( rate, 0.1 );
					float pct = (float)filesProcessed / totalFiles * 100f;
					long memMB = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
					
					Log.Info( $"  [GPU] {filesProcessed}/{totalFiles} ({pct:F1}%) | {instancesAdded} SceneObjects | {rate:F1} files/sec | ETA: {remaining:F0}s | RAM: {memMB}MB" );
				}
			}
			
			batchStages.Clear();
			
			if ( (batchStart / PARSE_BATCH_SIZE) % 3 == 2 )
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
			}
		}
		
		// Store reference to prevent GC (we need a way to persist this)
		_activeInstancedRenderer = instancedRenderer;
		
		stopwatch.Stop();
		Log.Info( $"=== [GPU] Scene loaded in {stopwatch.Elapsed.TotalSeconds:F1}s ===" );
		Log.Info( $"  {modelsCreated} unique models, {instancesAdded} total instances" );
		Log.Info( $"  {instancedRenderer.TotalInstances} SceneObjects (no GameObjects, no collision)" );
		Log.Info( $"  {failed} failed files" );
	}
	
	// Keep reference to prevent garbage collection
	private static Usd.UsdInstancedSceneRenderer _activeInstancedRenderer;
	private static Usd.UsdBatchRenderer _activeBatchRenderer;
	
	/// <summary>
	/// True GPU batching - single component handles all instances via Graphics.DrawModelInstanced
	/// </summary>
	private static async Task LoadFromIndexBatchAsync( string indexPath, int maxMeshes )
	{
		var stopwatch = Stopwatch.StartNew();
		
		Log.Info( $"[BATCH] Loading index from: {indexPath}" );
		
		UsdSceneIndex index;
		try
		{
			index = UsdSceneIndex.Load( indexPath );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to load index: {ex.Message}" );
			return;
		}
		
		Log.Info( $"Index loaded in {stopwatch.ElapsedMilliseconds}ms: {index.Meshes.Count} meshes" );
		
		float scale = 0.0254f * 39.3701f;
		
		// Group meshes by source file
		var meshesBySourceFile = new Dictionary<string, List<(IndexedMeshRef mesh, Transform scaledTransform)>>();
		
		int totalMeshes = 0;
		foreach ( var mesh in index.Meshes )
		{
			if ( totalMeshes >= maxMeshes )
				break;
			
			if ( !meshesBySourceFile.TryGetValue( mesh.SourcePath, out var list ) )
			{
				list = new List<(IndexedMeshRef, Transform)>();
				meshesBySourceFile[mesh.SourcePath] = list;
			}
			
			var scaledTransform = new Transform(
				mesh.Position * scale,
				mesh.Rotation,
				mesh.Scale * scale
			);
			
			list.Add( (mesh, scaledTransform) );
			totalMeshes++;
		}
		
		Log.Info( $"[BATCH] {totalMeshes} instances across {meshesBySourceFile.Count} source files" );
		
		// Create a single GameObject with the batch renderer component
		using var sceneScope = SceneEditorSession.Scope();
		
		var rootGo = new GameObject( false, "USD Batch Renderer" );
		rootGo.WorldPosition = Vector3.Zero;
		
		var batchRenderer = rootGo.Components.Create<Usd.UsdBatchRenderer>();
		
		// Model cache
		var modelCache = new Dictionary<string, Model>();
		int filesProcessed = 0;
		int modelsCreated = 0;
		int instancesAdded = 0;
		int failed = 0;
		
		const int PARSE_BATCH_SIZE = 2000;
		const int LOG_INTERVAL = 100;
		var sourceFiles = meshesBySourceFile.Keys.ToList();
		int totalFiles = sourceFiles.Count;
		
		Log.Info( $"[BATCH] Processing {totalFiles} geometry files..." );
		
		for ( int batchStart = 0; batchStart < totalFiles; batchStart += PARSE_BATCH_SIZE )
		{
			int batchEnd = Math.Min( batchStart + PARSE_BATCH_SIZE, totalFiles );
			var batchFiles = sourceFiles.Skip( batchStart ).Take( batchEnd - batchStart ).ToList();
			
			var batchStages = new ConcurrentDictionary<string, Usd.Schema.UsdStage>();
			await Task.Run( () =>
			{
				Parallel.ForEach( batchFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, sourceFile =>
				{
					try
					{
						if ( File.Exists( sourceFile ) )
						{
							var stage = UsdaParser.ParseFile( sourceFile );
							batchStages[sourceFile] = stage;
						}
					}
					catch
					{
						System.Threading.Interlocked.Increment( ref failed );
					}
				} );
			} );
			
			foreach ( var sourceFile in batchFiles )
			{
				filesProcessed++;
				
				if ( !batchStages.TryGetValue( sourceFile, out var stage ) )
					continue;
				
				var meshInstances = meshesBySourceFile[sourceFile];
				var missingModels = new HashSet<string>();
				
				foreach ( var (meshRef, _) in meshInstances )
				{
					var cacheKey = $"{sourceFile}|{meshRef.MeshName}";
					if ( !modelCache.ContainsKey( cacheKey ) )
						missingModels.Add( meshRef.MeshName );
				}
				
				if ( missingModels.Count > 0 )
				{
					foreach ( var prim in stage.Traverse() )
					{
						if ( prim.TypeName != "Mesh" )
							continue;
						
						if ( !missingModels.Contains( prim.Name ) )
							continue;
						
						var usdMesh = prim as Usd.Schema.UsdMesh ?? ConvertToMesh( prim );
						if ( usdMesh == null )
							continue;
						
						var model = BuildModelFromMesh( usdMesh );
						if ( model != null )
						{
							var cacheKey = $"{sourceFile}|{prim.Name}";
							modelCache[cacheKey] = model;
							modelsCreated++;
						}
					}
				}
				
				// Add instances to batch renderer (deferred until Build())
				foreach ( var (meshRef, transform) in meshInstances )
				{
					var cacheKey = $"{sourceFile}|{meshRef.MeshName}";
					if ( !modelCache.TryGetValue( cacheKey, out var model ) )
						continue;
					
					batchRenderer.AddInstance( model, transform );
					instancesAdded++;
				}
				
				if ( filesProcessed % LOG_INTERVAL == 0 )
				{
					double elapsed = stopwatch.Elapsed.TotalSeconds;
					double rate = filesProcessed / elapsed;
					double remaining = (totalFiles - filesProcessed) / Math.Max( rate, 0.1 );
					float pct = (float)filesProcessed / totalFiles * 100f;
					long memMB = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
					
					Log.Info( $"  [BATCH] {filesProcessed}/{totalFiles} ({pct:F1}%) | {instancesAdded} instances | {rate:F1} files/sec | ETA: {remaining:F0}s | RAM: {memMB}MB" );
				}
			}
			
			batchStages.Clear();
			
			if ( (batchStart / PARSE_BATCH_SIZE) % 3 == 2 )
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
			}
		}
		
		// Build the batch renderer (compiles transform arrays)
		batchRenderer.Build();
		rootGo.Enabled = true;
		
		_activeBatchRenderer = batchRenderer;
		
		stopwatch.Stop();
		Log.Info( $"=== [BATCH] Scene loaded in {stopwatch.Elapsed.TotalSeconds:F1}s ===" );
		Log.Info( $"  {modelsCreated} unique models, {instancesAdded} total instances" );
		Log.Info( $"  {batchRenderer.UniqueModels} draw calls (one per unique model)" );
		Log.Info( $"  SINGLE COMPONENT handles all rendering!" );
	}
	
	private static Usd.Schema.UsdMesh ConvertToMesh( Usd.Schema.UsdPrim prim )
	{
		var mesh = new Usd.Schema.UsdMesh
		{
			Name = prim.Name,
			Path = prim.Path,
			TypeName = "Mesh",
			Specifier = prim.Specifier,
			Parent = prim.Parent
		};
		
		foreach ( var attr in prim.Attributes )
			mesh.Attributes[attr.Key] = attr.Value;
		
		foreach ( var meta in prim.Metadata )
			mesh.Metadata[meta.Key] = meta.Value;
		
		foreach ( var rel in prim.Relationships )
			mesh.Relationships.Add( rel );
		
		return mesh;
	}
	
	/// <summary>
	/// Build a model from a USD stage (for testing)
	/// </summary>
	private static Model BuildModelFromStage( Usd.Schema.UsdStage stage, string name )
	{
		var builder = Model.Builder.WithName( name );
		var allVertices = new System.Collections.Generic.List<Vector3>();
		var allIndices = new System.Collections.Generic.List<int>();
		var meshCount = 0;
		
		foreach ( var prim in stage.Traverse() )
		{
			if ( prim.TypeName != "Mesh" )
				continue;
			
			var usdMesh = prim as Usd.Schema.UsdMesh ?? ConvertToMesh( prim );
			if ( usdMesh == null )
				continue;
			
			// Check if mesh should be centered (skeleton-bound or Maya-style bind pose mesh)
			bool shouldCenter = usdMesh.HasSkeletonBinding;
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
			
			var mesh = CreateMesh( usdMesh, allVertices, allIndices, shouldCenter );
			if ( mesh != null )
			{
				builder.AddMesh( mesh );
				meshCount++;
			}
		}
		
		if ( meshCount == 0 )
			return null;
		
		if ( allVertices.Count > 0 && allIndices.Count > 0 )
		{
			builder.AddTraceMesh( allVertices, allIndices );
		}
		
		return builder.Create();
	}
	
	private static Mesh CreateMesh( Usd.Schema.UsdMesh usdMesh, System.Collections.Generic.List<Vector3> traceVertices, System.Collections.Generic.List<int> traceIndices, bool centerMesh = false )
	{
		var points = usdMesh.Points;
		if ( points.Count == 0 )
			return null;
		
		var faceVertexIndices = usdMesh.FaceVertexIndices;
		if ( faceVertexIndices.Count == 0 )
			return null;
		
		var triangleIndices = usdMesh.GetTriangulatedIndices();
		var faceVertexTriIndices = usdMesh.GetTriangulatedFaceVertexIndices();
		var expandedNormals = usdMesh.GetExpandedNormals();
		var expandedUVs = usdMesh.GetExpandedUVs();
		
		// Center on X/Y only - skeleton-bound meshes have vertices at bind pose, not origin
		// Don't touch Z since objects sit on ground
		Vector3 usdCenter = Vector3.Zero;
		if ( centerMesh )
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
		
		var vertices = new System.Collections.Generic.List<SimpleVertex>();
		var indices = new System.Collections.Generic.List<int>();
		var vertexMap = new System.Collections.Generic.Dictionary<(int, int), int>();
		
		int traceVertexOffset = traceVertices.Count;
		
		for ( int i = 0; i < triangleIndices.Count; i++ )
		{
			int pointIdx = triangleIndices[i];
			int faceVertIdx = faceVertexTriIndices[i];
			
			var usdPosition = points[pointIdx];
			
			// Subtract center before coordinate conversion (for skeleton-bound meshes)
			if ( centerMesh )
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
		
		foreach ( var v in vertices )
			traceVertices.Add( v.position );
		
		foreach ( var idx in indices )
			traceIndices.Add( traceVertexOffset + idx );
		
		var material = Material.Create( "model", "simple_color" );
		material?.Set( "Color", Texture.White );
		var mesh = new Mesh( material );
		
		mesh.Bounds = BBox.FromPoints( vertices.Select( v => v.position ) );
		mesh.CreateVertexBuffer( vertices.Count, SimpleVertex.Layout, vertices );
		mesh.CreateIndexBuffer( indices.Count, indices );
		
		return mesh;
	}
	
	private static int _buildMeshLogCount = 0;
	private static Model BuildModelFromMesh( Usd.Schema.UsdMesh usdMesh )
	{
		// Skip guide/helper meshes (brushes, volumes, etc.)
		if ( !usdMesh.IsRenderable )
		{
			if ( _buildMeshLogCount++ < 10 )
				Log.Info( $"    Mesh '{usdMesh.Name}' skipped: not renderable (purpose={usdMesh.Purpose})" );
			return null;
		}
		
		var points = usdMesh.Points;
		var faceIndices = usdMesh.FaceVertexIndices;
		
		// Check if this mesh actually has geometry data
		if ( points.Count == 0 || faceIndices.Count == 0 )
		{
			if ( _buildMeshLogCount++ < 10 )
				Log.Info( $"    Mesh '{usdMesh.Name}' skipped: no geometry (points={points.Count}, indices={faceIndices.Count})" );
			return null;
		}
		
		var allVertices = new System.Collections.Generic.List<Vector3>();
		var allIndices = new System.Collections.Generic.List<int>();
		
		// Skeleton-bound and Maya-style meshes have verts at bind pose, not origin - need centering
		bool shouldCenter = usdMesh.HasSkeletonBinding;
		
		// Maya exports: polySurfaceShape###, pPlaneShape###, geoShape###
		if ( !shouldCenter )
		{
			bool isMayaBindPoseMesh = 
				System.Text.RegularExpressions.Regex.IsMatch( usdMesh.Name, @"^polySurfaceShape\d*$" ) ||
				System.Text.RegularExpressions.Regex.IsMatch( usdMesh.Name, @"^pPlaneShape\d*$" ) ||
				System.Text.RegularExpressions.Regex.IsMatch( usdMesh.Name, @"^geoShape\d*$" );
			
			if ( isMayaBindPoseMesh && usdMesh.Extent.HasValue )
			{
				var min = usdMesh.Extent.Value.Min;
				var max = usdMesh.Extent.Value.Max;
				float centerY = (min.y + max.y) / 2f;  // USD Y becomes s&box X
				float centerX = (min.x + max.x) / 2f;  // USD X becomes s&box -Y
				
				// Only center if there's actually a significant offset
				if ( Math.Abs( centerY ) > 10f || Math.Abs( centerX ) > 10f )
				{
					shouldCenter = true;
					if ( _buildMeshLogCount++ < 20 )
					{
						Log.Info( $"  [MESH_CENTER] Centering Maya mesh '{usdMesh.Name}' - USD center=({centerX:F1}, {centerY:F1})" );
					}
				}
			}
		}
		
		var mesh = CreateMesh( usdMesh, allVertices, allIndices, shouldCenter );
		if ( mesh == null )
			return null;
		
		var builder = Model.Builder.WithName( usdMesh.Name );
		builder.AddMesh( mesh );
		
		if ( allVertices.Count > 0 && allIndices.Count > 0 )
		{
			builder.AddTraceMesh( allVertices, allIndices );
		}
		
		var model = builder.Create();
		
		if ( model == null || model.IsError )
			return null;
		
		return model;
	}
}
