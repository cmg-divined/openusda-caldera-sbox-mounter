namespace Sandbox.Usd.Parser;

/// <summary>
/// Token types for USDA lexer
/// </summary>
public enum UsdaTokenType
{
	// Literals
	Identifier,
	String,
	Integer,
	Float,
	
	// Structural
	OpenParen,      // (
	CloseParen,     // )
	OpenBracket,    // [
	CloseBracket,   // ]
	OpenBrace,      // {
	CloseBrace,     // }
	
	// Operators
	Equals,         // =
	Comma,          // ,
	Colon,          // :
	Dot,            // .
	At,             // @
	
	// Special
	AssetPath,      // @path/to/file.usda@
	Path,           // </path/to/prim>
	Comment,
	Newline,
	EndOfFile,
	
	// Keywords are parsed as Identifiers then checked
}

/// <summary>
/// A single token from the USDA lexer
/// </summary>
public readonly struct UsdaToken
{
	public UsdaTokenType Type { get; }
	public string Value { get; }
	public int Line { get; }
	public int Column { get; }
	
	public UsdaToken( UsdaTokenType type, string value, int line, int column )
	{
		Type = type;
		Value = value;
		Line = line;
		Column = column;
	}
	
	public bool IsKeyword( string keyword ) => Type == UsdaTokenType.Identifier && Value == keyword;
	
	public override string ToString() => $"{Type}({Value}) at {Line}:{Column}";
}


