using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Sandbox.Usd.Schema;

namespace Sandbox.Usd.Parser;

/// <summary>
/// Parser for USDA (text-based USD) files
/// </summary>
public class UsdaParser
{
	private readonly List<UsdaToken> _tokens;
	private int _position;
	private readonly string _basePath;
	private readonly HashSet<string> _loadedFiles = new();
	
	/// <summary>
	/// Whether to resolve and load referenced files
	/// </summary>
	public bool ResolveReferences { get; set; } = true;
	
	/// <summary>
	/// Maximum depth for reference resolution (to prevent infinite loops)
	/// </summary>
	public int MaxReferenceDepth { get; set; } = 32;
	
	
	public UsdaParser( string source, string filePath = null )
	{
		var tokenizer = new UsdaTokenizer( source );
		_tokens = new List<UsdaToken>( tokenizer.Tokenize() );
		_basePath = string.IsNullOrEmpty( filePath ) ? "" : Path.GetDirectoryName( filePath );
	}
	
	/// <summary>
	/// Parse a USDA file from disk
	/// </summary>
	public static UsdStage ParseFile( string filePath )
	{
		var source = File.ReadAllText( filePath );
		var parser = new UsdaParser( source, filePath );
		var stage = parser.Parse();
		stage.FilePath = filePath;
		return stage;
	}
	
	/// <summary>
	/// Parse the USDA source and return a stage
	/// </summary>
	public UsdStage Parse()
	{
		var stage = new UsdStage();
		
		// Parse header: #usda 1.0
		if ( Current.Type == UsdaTokenType.Comment || (Current.Type == UsdaTokenType.Identifier && Current.Value == "usda") )
		{
			// Skip the #usda 1.0 line - it's parsed as identifier after # is stripped
			while ( Current.Type != UsdaTokenType.OpenParen && Current.Type != UsdaTokenType.EndOfFile )
			{
				Advance();
			}
		}
		
		// Parse stage metadata in parentheses
		if ( Current.Type == UsdaTokenType.OpenParen )
		{
			ParseStageMetadata( stage );
		}
		
		// Parse root prims
		while ( Current.Type != UsdaTokenType.EndOfFile )
		{
			var prim = ParsePrim( null );
			if ( prim != null )
			{
				prim.Path = $"/{prim.Name}";
				stage.RootPrims.Add( prim );
				RegisterPrimRecursive( stage, prim );
			}
		}
		
		return stage;
	}
	
	private void RegisterPrimRecursive( UsdStage stage, UsdPrim prim )
	{
		stage.RegisterPrim( prim );
		foreach ( var child in prim.Children )
		{
			RegisterPrimRecursive( stage, child );
		}
	}
	
	private void ParseStageMetadata( UsdStage stage )
	{
		Expect( UsdaTokenType.OpenParen );
		
		while ( Current.Type != UsdaTokenType.CloseParen && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.String )
			{
				// Documentation string
				stage.Documentation = Current.Value;
				Advance();
				continue;
			}
			
			if ( Current.Type != UsdaTokenType.Identifier )
			{
				Advance();
				continue;
			}
			
			var name = Current.Value;
			Advance();
			
			if ( Current.Type != UsdaTokenType.Equals )
			{
				continue;
			}
			Advance();
			
			switch ( name )
			{
				case "defaultPrim":
					stage.DefaultPrim = ExpectString();
					break;
				case "upAxis":
					stage.UpAxis = ExpectString();
					break;
				case "metersPerUnit":
					stage.MetersPerUnit = ExpectNumber();
					break;
				case "timeCodesPerSecond":
					stage.TimeCodesPerSecond = ExpectNumber();
					break;
				case "framesPerSecond":
					stage.FramesPerSecond = ExpectNumber();
					break;
				case "startTimeCode":
					stage.StartTimeCode = ExpectNumber();
					break;
				case "endTimeCode":
					stage.EndTimeCode = ExpectNumber();
					break;
				case "subLayers":
					var layers = ParseStringArray();
					stage.SubLayers.AddRange( layers );
					break;
				default:
					// Skip unknown metadata
					SkipValue();
					break;
			}
		}
		
		Expect( UsdaTokenType.CloseParen );
	}
	
	private UsdPrim ParsePrim( UsdPrim parent )
	{
		// Expect: def/over/class TypeName "name" ( metadata ) { children }
		if ( Current.Type != UsdaTokenType.Identifier )
			return null;
		
		var specifier = Current.Value;
		if ( specifier != "def" && specifier != "over" && specifier != "class" )
			return null;
		
		Advance();
		
		// Type name (optional for 'over')
		string typeName = null;
		if ( Current.Type == UsdaTokenType.Identifier )
		{
			typeName = Current.Value;
			Advance();
		}
		
		// Prim name
		var name = ExpectString();
		
		// Create appropriate prim type
		UsdPrim prim;
		if ( typeName == "Mesh" )
			prim = new UsdMesh();
		else
			prim = new UsdPrim();
		
		prim.Name = name;
		prim.TypeName = typeName;
		prim.Specifier = specifier;
		prim.Parent = parent;
		
		if ( parent != null )
			prim.Path = $"{parent.Path}/{name}";
		
		// Parse prim metadata in parentheses
		if ( Current.Type == UsdaTokenType.OpenParen )
		{
			ParsePrimMetadata( prim );
		}
		
		// Parse prim body in braces
		if ( Current.Type == UsdaTokenType.OpenBrace )
		{
			ParsePrimBody( prim );
		}
		
		return prim;
	}
	
	private void ParsePrimMetadata( UsdPrim prim )
	{
		Expect( UsdaTokenType.OpenParen );
		
		while ( Current.Type != UsdaTokenType.CloseParen && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type != UsdaTokenType.Identifier )
			{
				Advance();
				continue;
			}
			
			// Check for 'prepend' or 'append' modifier
			var modifier = "";
			if ( Current.Value == "prepend" || Current.Value == "append" )
			{
				modifier = Current.Value;
				Advance();
			}
			
			var name = Current.Value;
			Advance();
			
			if ( Current.Type != UsdaTokenType.Equals )
			{
				continue;
			}
			Advance();
			
			switch ( name )
			{
				case "references":
					var refs = ParseAssetPathArray();
					prim.References.AddRange( refs );
					break;
				case "payload":
					var payloads = ParseAssetPathArray();
					prim.Payloads.AddRange( payloads );
					break;
				case "inherits":
					var inherits = ParsePathArray();
					prim.Inherits.AddRange( inherits );
					break;
				case "apiSchemas":
					var schemas = ParseTokenArray();
					prim.ApiSchemas.AddRange( schemas );
					break;
				case "instanceable":
					prim.Metadata["instanceable"] = new UsdBool( ExpectBool() );
					break;
				case "kind":
					prim.Metadata["kind"] = new UsdToken( ExpectString() );
					break;
				case "variants":
					ParseVariantSelections( prim );
					break;
				case "variantSets":
					var variantSets = ParseStringArray();
					// Just store the names, variant contents are parsed in body
					break;
				case "customData":
					SkipDictionary();
					break;
				default:
					SkipValue();
					break;
			}
		}
		
		Expect( UsdaTokenType.CloseParen );
	}
	
	private void ParsePrimBody( UsdPrim prim )
	{
		Expect( UsdaTokenType.OpenBrace );
		
		while ( Current.Type != UsdaTokenType.CloseBrace && Current.Type != UsdaTokenType.EndOfFile )
		{
			// Check for child prim
			if ( Current.Type == UsdaTokenType.Identifier )
			{
				var keyword = Current.Value;
				
				if ( keyword == "def" || keyword == "over" || keyword == "class" )
				{
					var child = ParsePrim( prim );
					if ( child != null )
					{
						prim.Children.Add( child );
					}
					continue;
				}
				
				if ( keyword == "variantSet" )
				{
					ParseVariantSet( prim );
					continue;
				}
				
				// Parse attribute
				ParseAttribute( prim );
				continue;
			}
			
			Advance();
		}
		
		Expect( UsdaTokenType.CloseBrace );
	}
	
	private void ParseAttribute( UsdPrim prim )
	{
		// Format: type name = value
		// Or: uniform type name = value
		// Or just: type[] name = [values]
		// Or: prepend rel name = <path>
		
		// Check for 'prepend' or 'append' modifier
		if ( Current.Value == "prepend" || Current.Value == "append" )
		{
			Advance();
		}
		
		// Check for 'uniform' keyword (skip it - we don't use it currently)
		if ( Current.Value == "uniform" )
		{
			Advance();
		}
		
		if ( Current.Type != UsdaTokenType.Identifier )
		{
			Advance();
			return;
		}
		
		var typeName = Current.Value;
		Advance();
		
		// Handle relationships (rel type)
		if ( typeName == "rel" )
		{
			ParseRelationship( prim );
			return;
		}
		
		// Check for array type
		bool isArray = false;
		if ( Current.Type == UsdaTokenType.OpenBracket )
		{
			Advance();
			Expect( UsdaTokenType.CloseBracket );
			isArray = true;
		}
		
		// Attribute name
		if ( Current.Type != UsdaTokenType.Identifier )
		{
			return;
		}
		
		var attrName = Current.Value;
		Advance();
		
		// Handle namespaced attributes like primvars:st
		while ( Current.Type == UsdaTokenType.Colon )
		{
			Advance();
			if ( Current.Type == UsdaTokenType.Identifier )
			{
				attrName += ":" + Current.Value;
				Advance();
			}
		}
		
		if ( Current.Type != UsdaTokenType.Equals )
		{
			return;
		}
		Advance();
		
		// Parse the value based on type
		var value = ParseTypedValue( typeName, isArray );
		if ( value != null )
		{
			prim.Attributes[attrName] = value;
		}
	}
	
	private void ParseRelationship( UsdPrim prim )
	{
		// Format: rel name = <path>
		// Or: rel name = [<path1>, <path2>]
		
		if ( Current.Type != UsdaTokenType.Identifier )
		{
			return;
		}
		
		var relName = Current.Value;
		Advance();
		
		// Handle namespaced relationship names like skel:skeleton
		while ( Current.Type == UsdaTokenType.Colon )
		{
			Advance();
			if ( Current.Type == UsdaTokenType.Identifier )
			{
				relName += ":" + Current.Value;
				Advance();
			}
		}
		
		if ( Current.Type != UsdaTokenType.Equals )
		{
			return;
		}
		Advance();
		
		var relationship = new UsdRelationship { Name = relName };
		
		// Parse single path or array of paths
		if ( Current.Type == UsdaTokenType.OpenBracket )
		{
			// Array of paths
			Advance();
			while ( Current.Type != UsdaTokenType.CloseBracket && Current.Type != UsdaTokenType.EndOfFile )
			{
				if ( Current.Type == UsdaTokenType.Path )
				{
					relationship.Targets.Add( Current.Value );
					Advance();
				}
				else if ( Current.Type == UsdaTokenType.Comma )
				{
					Advance();
				}
				else
				{
					Advance();
				}
			}
			if ( Current.Type == UsdaTokenType.CloseBracket )
				Advance();
		}
		else if ( Current.Type == UsdaTokenType.Path )
		{
			// Single path like </path/to/prim>
			relationship.Targets.Add( Current.Value );
			Advance();
		}
		
		if ( relationship.Targets.Count > 0 )
		{
			prim.Relationships.Add( relationship );
		}
	}
	
	private UsdValue ParseTypedValue( string typeName, bool isArray )
	{
		if ( isArray )
		{
			return ParseTypedArray( typeName );
		}
		
		switch ( typeName )
		{
			case "bool":
				return new UsdBool( ExpectBool() );
			case "int":
				return new UsdInt( (int)ExpectNumber() );
			case "float":
			case "half":
				return new UsdFloat( (float)ExpectNumber() );
			case "double":
				return new UsdDouble( ExpectNumber() );
			case "string":
				return new UsdString( ExpectString() );
			case "token":
				return new UsdToken( ExpectString() );
			case "asset":
				return ParseAssetPath();
			case "float2":
			case "double2":
			case "texCoord2f":
				return ParseVector2();
			case "float3":
			case "double3":
			case "point3f":
			case "normal3f":
			case "vector3f":
			case "color3f":
				return ParseVector3();
			case "float4":
			case "double4":
			case "quath":
			case "quatf":
			case "quatd":
				return ParseVector4();
			case "matrix4d":
				return ParseMatrix4();
			default:
				SkipValue();
				return null;
		}
	}
	
	private UsdValue ParseTypedArray( string typeName )
	{
		if ( Current.Type != UsdaTokenType.OpenBracket )
		{
			SkipValue();
			return null;
		}
		Advance();
		
		switch ( typeName )
		{
			case "int":
				return ParseIntArrayContents();
			case "float":
			case "half":
			case "double":
				return ParseFloatArrayContents();
			case "string":
				return ParseStringArrayContents();
			case "token":
				return ParseTokenArrayContents();
			case "float2":
			case "double2":
			case "texCoord2f":
				return ParseVector2ArrayContents();
			case "float3":
			case "double3":
			case "point3f":
			case "normal3f":
			case "vector3f":
			case "color3f":
				return ParseVector3ArrayContents();
			case "matrix4d":
				return ParseMatrix4ArrayContents();
			default:
				SkipToCloseBracket();
				return null;
		}
	}
	
	private UsdIntArray ParseIntArrayContents()
	{
		var result = new UsdIntArray();
		
		while ( Current.Type != UsdaTokenType.CloseBracket && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.Integer || Current.Type == UsdaTokenType.Float )
			{
				result.Values.Add( (int)double.Parse( Current.Value, CultureInfo.InvariantCulture ) );
				Advance();
			}
			else if ( Current.Type == UsdaTokenType.Comma )
			{
				Advance();
			}
			else
			{
				Advance();
			}
		}
		
		Expect( UsdaTokenType.CloseBracket );
		return result;
	}
	
	private UsdFloatArray ParseFloatArrayContents()
	{
		var result = new UsdFloatArray();
		
		while ( Current.Type != UsdaTokenType.CloseBracket && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.Integer || Current.Type == UsdaTokenType.Float )
			{
				result.Values.Add( (float)double.Parse( Current.Value, CultureInfo.InvariantCulture ) );
				Advance();
			}
			else if ( Current.Type == UsdaTokenType.Comma )
			{
				Advance();
			}
			else
			{
				Advance();
			}
		}
		
		Expect( UsdaTokenType.CloseBracket );
		return result;
	}
	
	private UsdStringArray ParseStringArrayContents()
	{
		var result = new UsdStringArray();
		
		while ( Current.Type != UsdaTokenType.CloseBracket && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.String )
			{
				result.Values.Add( Current.Value );
				Advance();
			}
			else if ( Current.Type == UsdaTokenType.Comma )
			{
				Advance();
			}
			else
			{
				Advance();
			}
		}
		
		Expect( UsdaTokenType.CloseBracket );
		return result;
	}
	
	private UsdTokenArray ParseTokenArrayContents()
	{
		var result = new UsdTokenArray();
		
		while ( Current.Type != UsdaTokenType.CloseBracket && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.String )
			{
				result.Values.Add( Current.Value );
				Advance();
			}
			else if ( Current.Type == UsdaTokenType.Comma )
			{
				Advance();
			}
			else
			{
				Advance();
			}
		}
		
		Expect( UsdaTokenType.CloseBracket );
		return result;
	}
	
	private UsdVector2Array ParseVector2ArrayContents()
	{
		var result = new UsdVector2Array();
		
		while ( Current.Type != UsdaTokenType.CloseBracket && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.OpenParen )
			{
				Advance();
				var x = (float)ExpectNumber();
				ExpectComma();
				var y = (float)ExpectNumber();
				Expect( UsdaTokenType.CloseParen );
				result.Values.Add( new Vector2( x, y ) );
			}
			else if ( Current.Type == UsdaTokenType.Comma )
			{
				Advance();
			}
			else
			{
				Advance();
			}
		}
		
		Expect( UsdaTokenType.CloseBracket );
		return result;
	}
	
	private UsdVector3Array ParseVector3ArrayContents()
	{
		var result = new UsdVector3Array();
		
		while ( Current.Type != UsdaTokenType.CloseBracket && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.OpenParen )
			{
				Advance();
				var x = (float)ExpectNumber();
				ExpectComma();
				var y = (float)ExpectNumber();
				ExpectComma();
				var z = (float)ExpectNumber();
				Expect( UsdaTokenType.CloseParen );
				result.Values.Add( new Vector3( x, y, z ) );
			}
			else if ( Current.Type == UsdaTokenType.Comma )
			{
				Advance();
			}
			else
			{
				Advance();
			}
		}
		
		Expect( UsdaTokenType.CloseBracket );
		return result;
	}
	
	private UsdMatrix4Array ParseMatrix4ArrayContents()
	{
		var result = new UsdMatrix4Array();
		
		while ( Current.Type != UsdaTokenType.CloseBracket && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.OpenParen )
			{
				// Parse single matrix
				var matrix = ParseMatrix4();
				result.Values.Add( matrix.Value );
			}
			else if ( Current.Type == UsdaTokenType.Comma )
			{
				Advance();
			}
			else
			{
				Advance();
			}
		}
		
		Expect( UsdaTokenType.CloseBracket );
		return result;
	}
	
	private UsdVector2 ParseVector2()
	{
		Expect( UsdaTokenType.OpenParen );
		var x = (float)ExpectNumber();
		ExpectComma();
		var y = (float)ExpectNumber();
		Expect( UsdaTokenType.CloseParen );
		return new UsdVector2( x, y );
	}
	
	private UsdVector3 ParseVector3()
	{
		Expect( UsdaTokenType.OpenParen );
		var x = (float)ExpectNumber();
		ExpectComma();
		var y = (float)ExpectNumber();
		ExpectComma();
		var z = (float)ExpectNumber();
		Expect( UsdaTokenType.CloseParen );
		return new UsdVector3( x, y, z );
	}
	
	private UsdVector4 ParseVector4()
	{
		Expect( UsdaTokenType.OpenParen );
		var x = (float)ExpectNumber();
		ExpectComma();
		var y = (float)ExpectNumber();
		ExpectComma();
		var z = (float)ExpectNumber();
		ExpectComma();
		var w = (float)ExpectNumber();
		Expect( UsdaTokenType.CloseParen );
		return new UsdVector4( x, y, z, w );
	}
	
	private UsdMatrix4 ParseMatrix4()
	{
		// Format: ( (r0c0, r0c1, r0c2, r0c3), (r1c0, ...), ... )
		Expect( UsdaTokenType.OpenParen );
		
		var values = new float[16];
		for ( int row = 0; row < 4; row++ )
		{
			if ( row > 0 ) ExpectComma();
			Expect( UsdaTokenType.OpenParen );
			for ( int col = 0; col < 4; col++ )
			{
				if ( col > 0 ) ExpectComma();
				values[row * 4 + col] = (float)ExpectNumber();
			}
			Expect( UsdaTokenType.CloseParen );
		}
		
		Expect( UsdaTokenType.CloseParen );
		
		var matrix = new Matrix(
			values[0], values[1], values[2], values[3],
			values[4], values[5], values[6], values[7],
			values[8], values[9], values[10], values[11],
			values[12], values[13], values[14], values[15]
		);
		
		return new UsdMatrix4( matrix );
	}
	
	private UsdAssetPath ParseAssetPath()
	{
		if ( Current.Type == UsdaTokenType.AssetPath )
		{
			var path = Current.Value;
			Advance();
			return new UsdAssetPath( path );
		}
		return null;
	}
	
	private List<string> ParseAssetPathArray()
	{
		var result = new List<string>();
		
		// Could be single path or array
		if ( Current.Type == UsdaTokenType.AssetPath )
		{
			result.Add( Current.Value );
			Advance();
			return result;
		}
		
		if ( Current.Type != UsdaTokenType.OpenBracket )
			return result;
		
		Advance();
		
		while ( Current.Type != UsdaTokenType.CloseBracket && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.AssetPath )
			{
				result.Add( Current.Value );
				Advance();
			}
			else if ( Current.Type == UsdaTokenType.Comma )
			{
				Advance();
			}
			else
			{
				Advance();
			}
		}
		
		Expect( UsdaTokenType.CloseBracket );
		return result;
	}
	
	private List<string> ParsePathArray()
	{
		var result = new List<string>();
		
		if ( Current.Type != UsdaTokenType.OpenBracket )
			return result;
		
		Advance();
		
		while ( Current.Type != UsdaTokenType.CloseBracket && Current.Type != UsdaTokenType.EndOfFile )
		{
			// Paths look like </path/to/prim>
			if ( Current.Type == UsdaTokenType.Identifier )
			{
				// Could be path without < > 
				result.Add( Current.Value );
				Advance();
			}
			else if ( Current.Type == UsdaTokenType.Comma )
			{
				Advance();
			}
			else
			{
				Advance();
			}
		}
		
		Expect( UsdaTokenType.CloseBracket );
		return result;
	}
	
	private List<string> ParseTokenArray()
	{
		var result = new List<string>();
		
		if ( Current.Type != UsdaTokenType.OpenBracket )
			return result;
		
		Advance();
		
		while ( Current.Type != UsdaTokenType.CloseBracket && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.String || Current.Type == UsdaTokenType.Identifier )
			{
				result.Add( Current.Value );
				Advance();
			}
			else if ( Current.Type == UsdaTokenType.Comma )
			{
				Advance();
			}
			else
			{
				Advance();
			}
		}
		
		Expect( UsdaTokenType.CloseBracket );
		return result;
	}
	
	private List<string> ParseStringArray()
	{
		var result = new List<string>();
		
		if ( Current.Type != UsdaTokenType.OpenBracket )
		{
			// Single value
			if ( Current.Type == UsdaTokenType.String )
			{
				result.Add( Current.Value );
				Advance();
			}
			else if ( Current.Type == UsdaTokenType.AssetPath )
			{
				result.Add( Current.Value );
				Advance();
			}
			return result;
		}
		
		Advance();
		
		while ( Current.Type != UsdaTokenType.CloseBracket && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.String )
			{
				result.Add( Current.Value );
				Advance();
			}
			else if ( Current.Type == UsdaTokenType.AssetPath )
			{
				result.Add( Current.Value );
				Advance();
			}
			else if ( Current.Type == UsdaTokenType.Comma )
			{
				Advance();
			}
			else
			{
				Advance();
			}
		}
		
		Expect( UsdaTokenType.CloseBracket );
		return result;
	}
	
	private void ParseVariantSelections( UsdPrim prim )
	{
		if ( Current.Type != UsdaTokenType.OpenBrace )
			return;
		
		Advance();
		
		while ( Current.Type != UsdaTokenType.CloseBrace && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.Identifier )
			{
				var typeName = Current.Value; // e.g., "string"
				Advance();
				
				if ( Current.Type == UsdaTokenType.Identifier )
				{
					var variantSetName = Current.Value;
					Advance();
					
					if ( Current.Type == UsdaTokenType.Equals )
					{
						Advance();
						var variantName = ExpectString();
						prim.Variants[variantSetName] = variantName;
					}
				}
			}
			else
			{
				Advance();
			}
		}
		
		Expect( UsdaTokenType.CloseBrace );
	}
	
	private void ParseVariantSet( UsdPrim prim )
	{
		Advance(); // Skip 'variantSet'
		
		var setName = ExpectString();
		
		if ( !prim.VariantSets.ContainsKey( setName ) )
			prim.VariantSets[setName] = new Dictionary<string, UsdPrim>();
		
		Expect( UsdaTokenType.Equals );
		Expect( UsdaTokenType.OpenBrace );
		
		while ( Current.Type != UsdaTokenType.CloseBrace && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.String )
			{
				var variantName = Current.Value;
				Advance();
				
				// Create a temporary prim to hold variant content
				var variantPrim = new UsdPrim { Name = variantName };
				
				// Parse variant metadata in parens - THIS CONTAINS PAYLOADS!
				if ( Current.Type == UsdaTokenType.OpenParen )
				{
					ParseVariantMetadata( variantPrim );
				}
				
				// Parse variant body in braces (attributes, children)
				if ( Current.Type == UsdaTokenType.OpenBrace )
				{
					ParsePrimBody( variantPrim );
				}
				
				prim.VariantSets[setName][variantName] = variantPrim;
			}
			else
			{
				Advance();
			}
		}
		
		Expect( UsdaTokenType.CloseBrace );
	}
	
	private void ParseVariantMetadata( UsdPrim variantPrim )
	{
		Expect( UsdaTokenType.OpenParen );
		
		while ( Current.Type != UsdaTokenType.CloseParen && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.Identifier )
			{
				var keyword = Current.Value;
				Advance();
				
				// Handle "prepend payload = @...@" or "prepend references = @...@"
				if ( keyword == "prepend" && Current.Type == UsdaTokenType.Identifier )
				{
					var refType = Current.Value;
					Advance();
					
					if ( Current.Type == UsdaTokenType.Equals )
					{
						Advance();
						
						if ( refType == "payload" || refType == "payloads" )
						{
							if ( Current.Type == UsdaTokenType.AssetPath )
							{
								variantPrim.Payloads.Add( Current.Value );
								Advance();
							}
							else if ( Current.Type == UsdaTokenType.OpenBracket )
							{
								var paths = ParseAssetPathArray();
								variantPrim.Payloads.AddRange( paths );
							}
						}
						else if ( refType == "references" )
						{
							if ( Current.Type == UsdaTokenType.AssetPath )
							{
								variantPrim.References.Add( Current.Value );
								Advance();
							}
							else if ( Current.Type == UsdaTokenType.OpenBracket )
							{
								var paths = ParseAssetPathArray();
								variantPrim.References.AddRange( paths );
							}
						}
						else
						{
							SkipValue();
						}
					}
				}
				else if ( keyword == "payload" )
				{
					if ( Current.Type == UsdaTokenType.Equals )
					{
						Advance();
						if ( Current.Type == UsdaTokenType.AssetPath )
						{
							variantPrim.Payloads.Add( Current.Value );
							Advance();
						}
					}
				}
				else
				{
					// Skip other metadata
					if ( Current.Type == UsdaTokenType.Equals )
					{
						Advance();
						SkipValue();
					}
				}
			}
			else
			{
				Advance();
			}
		}
		
		Expect( UsdaTokenType.CloseParen );
	}
	
	private void SkipValue()
	{
		// Skip a single value (could be nested)
		int depth = 0;
		
		do
		{
			if ( Current.Type == UsdaTokenType.OpenParen || 
				 Current.Type == UsdaTokenType.OpenBracket || 
				 Current.Type == UsdaTokenType.OpenBrace )
			{
				depth++;
			}
			else if ( Current.Type == UsdaTokenType.CloseParen || 
					  Current.Type == UsdaTokenType.CloseBracket || 
					  Current.Type == UsdaTokenType.CloseBrace )
			{
				depth--;
				if ( depth < 0 ) return; // Don't consume closing bracket we don't own
			}
			
			Advance();
		} while ( depth > 0 && Current.Type != UsdaTokenType.EndOfFile );
	}
	
	private void SkipDictionary()
	{
		if ( Current.Type != UsdaTokenType.OpenBrace )
		{
			SkipValue();
			return;
		}
		
		int depth = 1;
		Advance();
		
		while ( depth > 0 && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.OpenBrace )
				depth++;
			else if ( Current.Type == UsdaTokenType.CloseBrace )
				depth--;
			Advance();
		}
	}
	
	private void SkipParentheses()
	{
		if ( Current.Type != UsdaTokenType.OpenParen )
			return;
		
		int depth = 1;
		Advance();
		
		while ( depth > 0 && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.OpenParen )
				depth++;
			else if ( Current.Type == UsdaTokenType.CloseParen )
				depth--;
			Advance();
		}
	}
	
	private void SkipToCloseBracket()
	{
		int depth = 1;
		
		while ( depth > 0 && Current.Type != UsdaTokenType.EndOfFile )
		{
			if ( Current.Type == UsdaTokenType.OpenBracket )
				depth++;
			else if ( Current.Type == UsdaTokenType.CloseBracket )
				depth--;
			Advance();
		}
	}
	
	// Helper methods
	private UsdaToken Current => _position < _tokens.Count ? _tokens[_position] : new UsdaToken( UsdaTokenType.EndOfFile, "", 0, 0 );
	
	private void Advance()
	{
		if ( _position < _tokens.Count )
			_position++;
	}
	
	private void Expect( UsdaTokenType type )
	{
		if ( Current.Type == type )
			Advance();
	}
	
	private void ExpectComma()
	{
		if ( Current.Type == UsdaTokenType.Comma )
			Advance();
	}
	
	private string ExpectString()
	{
		if ( Current.Type == UsdaTokenType.String )
		{
			var value = Current.Value;
			Advance();
			return value;
		}
		if ( Current.Type == UsdaTokenType.Identifier )
		{
			var value = Current.Value;
			Advance();
			return value;
		}
		return "";
	}
	
	private double ExpectNumber()
	{
		if ( Current.Type == UsdaTokenType.Integer || Current.Type == UsdaTokenType.Float )
		{
			var value = double.Parse( Current.Value, CultureInfo.InvariantCulture );
			Advance();
			return value;
		}
		return 0;
	}
	
	private bool ExpectBool()
	{
		if ( Current.Type == UsdaTokenType.Identifier )
		{
			var value = Current.Value.ToLower() == "true" || Current.Value == "1";
			Advance();
			return value;
		}
		if ( Current.Type == UsdaTokenType.Integer )
		{
			var value = Current.Value != "0";
			Advance();
			return value;
		}
		return false;
	}
}

