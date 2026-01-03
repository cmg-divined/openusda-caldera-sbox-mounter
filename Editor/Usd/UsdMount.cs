using System.Collections.Generic;
using System.Threading.Tasks;
using Sandbox.Mounting;

namespace Sandbox.Usd;

/// <summary>
/// s&box mount for loading OpenUSD content from the Caldera dataset
/// </summary>
public class UsdMount : BaseGameMount
{
	/// <summary>
	/// Base path where the USD content is located
	/// </summary>
	public const string BasePath = @"C:\OpenUSDA";
	
	public override string Ident => "openusd";
	
	public override string Title => "OpenUSD Caldera";
	
	protected override void Initialize( InitializeContext context )
	{
		// Check if the USD content directory exists
		IsInstalled = System.IO.Directory.Exists( BasePath );
	}
	
	protected override Task Mount( MountContext context )
	{
		if ( !System.IO.Directory.Exists( BasePath ) )
		{
			context.AddError( $"USD content path not found: {BasePath}" );
			return Task.CompletedTask;
		}
		
		// Find all geometry files
		int count = 0;
		foreach ( var file in System.IO.Directory.EnumerateFiles( BasePath, "*.geo.usda", System.IO.SearchOption.AllDirectories ) )
		{
			var relativePath = System.IO.Path.GetRelativePath( BasePath, file ).Replace( '\\', '/' );
			
			context.Add( ResourceType.Model, relativePath, new UsdModelResourceLoader
			{
				FullPath = file
			} );
			
			count++;
		}
		
		Log.Info( $"OpenUSD: Mounted {count} geometry files" );
		IsMounted = true;
		return Task.CompletedTask;
	}
	
	/// <summary>
	/// Get full path from a relative path
	/// </summary>
	public string GetFullPath( string relativePath )
	{
		return System.IO.Path.Combine( BasePath, relativePath.Replace( '/', '\\' ) );
	}
	
	/// <summary>
	/// Read file contents
	/// </summary>
	public string ReadFileText( string fullPath )
	{
		return System.IO.File.ReadAllText( fullPath );
	}
}
