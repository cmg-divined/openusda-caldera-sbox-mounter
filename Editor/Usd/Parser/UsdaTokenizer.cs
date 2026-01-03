using System;
using System.Collections.Generic;
using System.Text;

namespace Sandbox.Usd.Parser;

/// <summary>
/// Tokenizer/Lexer for USDA text format
/// </summary>
public class UsdaTokenizer
{
	private readonly string _source;
	private int _position;
	private int _line = 1;
	private int _column = 1;
	
	public UsdaTokenizer( string source )
	{
		_source = source ?? throw new ArgumentNullException( nameof(source) );
	}
	
	public IEnumerable<UsdaToken> Tokenize()
	{
		while ( _position < _source.Length )
		{
			var token = ReadNextToken();
			if ( token.HasValue )
			{
				// Skip comments and newlines for simplicity
				if ( token.Value.Type != UsdaTokenType.Comment )
				{
					yield return token.Value;
				}
			}
		}
		
		yield return new UsdaToken( UsdaTokenType.EndOfFile, "", _line, _column );
	}
	
	private UsdaToken? ReadNextToken()
	{
		SkipWhitespace();
		
		if ( _position >= _source.Length )
			return null;
		
		var startLine = _line;
		var startColumn = _column;
		var c = Current;
		
		// Single character tokens
		switch ( c )
		{
			case '(': Advance(); return new UsdaToken( UsdaTokenType.OpenParen, "(", startLine, startColumn );
			case ')': Advance(); return new UsdaToken( UsdaTokenType.CloseParen, ")", startLine, startColumn );
			case '[': Advance(); return new UsdaToken( UsdaTokenType.OpenBracket, "[", startLine, startColumn );
			case ']': Advance(); return new UsdaToken( UsdaTokenType.CloseBracket, "]", startLine, startColumn );
			case '{': Advance(); return new UsdaToken( UsdaTokenType.OpenBrace, "{", startLine, startColumn );
			case '}': Advance(); return new UsdaToken( UsdaTokenType.CloseBrace, "}", startLine, startColumn );
			case '=': Advance(); return new UsdaToken( UsdaTokenType.Equals, "=", startLine, startColumn );
			case ',': Advance(); return new UsdaToken( UsdaTokenType.Comma, ",", startLine, startColumn );
			case ':': Advance(); return new UsdaToken( UsdaTokenType.Colon, ":", startLine, startColumn );
			case '.': Advance(); return new UsdaToken( UsdaTokenType.Dot, ".", startLine, startColumn );
		}
		
		// Asset path: @...@
		if ( c == '@' )
		{
			return ReadAssetPath( startLine, startColumn );
		}
		
		// Prim path: <...>
		if ( c == '<' )
		{
			return ReadPath( startLine, startColumn );
		}
		
		// String literal
		if ( c == '"' )
		{
			return ReadString( startLine, startColumn );
		}
		
		// Comment
		if ( c == '#' )
		{
			return ReadComment( startLine, startColumn );
		}
		
		// Number (including negative)
		if ( char.IsDigit( c ) || (c == '-' && _position + 1 < _source.Length && (char.IsDigit( _source[_position + 1] ) || _source[_position + 1] == '.')) || (c == '+' && _position + 1 < _source.Length) )
		{
			return ReadNumber( startLine, startColumn );
		}
		
		// Identifier or keyword
		if ( char.IsLetter( c ) || c == '_' )
		{
			return ReadIdentifier( startLine, startColumn );
		}
		
		// Unknown character - skip it
		Advance();
		return null;
	}
	
	private UsdaToken ReadAssetPath( int startLine, int startColumn )
	{
		Advance(); // Skip opening @
		var sb = new StringBuilder();
		
		while ( _position < _source.Length && Current != '@' )
		{
			sb.Append( Current );
			Advance();
		}
		
		if ( _position < _source.Length )
			Advance(); // Skip closing @
		
		return new UsdaToken( UsdaTokenType.AssetPath, sb.ToString(), startLine, startColumn );
	}
	
	private UsdaToken ReadPath( int startLine, int startColumn )
	{
		Advance(); // Skip opening <
		var sb = new StringBuilder();
		
		while ( _position < _source.Length && Current != '>' )
		{
			sb.Append( Current );
			Advance();
		}
		
		if ( _position < _source.Length )
			Advance(); // Skip closing >
		
		return new UsdaToken( UsdaTokenType.Path, sb.ToString(), startLine, startColumn );
	}
	
	private UsdaToken ReadString( int startLine, int startColumn )
	{
		Advance(); // Skip opening quote
		var sb = new StringBuilder();
		
		while ( _position < _source.Length && Current != '"' )
		{
			if ( Current == '\\' && _position + 1 < _source.Length )
			{
				Advance();
				switch ( Current )
				{
					case 'n': sb.Append( '\n' ); break;
					case 'r': sb.Append( '\r' ); break;
					case 't': sb.Append( '\t' ); break;
					case '\\': sb.Append( '\\' ); break;
					case '"': sb.Append( '"' ); break;
					default: sb.Append( Current ); break;
				}
			}
			else
			{
				sb.Append( Current );
			}
			Advance();
		}
		
		if ( _position < _source.Length )
			Advance(); // Skip closing quote
		
		return new UsdaToken( UsdaTokenType.String, sb.ToString(), startLine, startColumn );
	}
	
	private UsdaToken ReadComment( int startLine, int startColumn )
	{
		var sb = new StringBuilder();
		
		while ( _position < _source.Length && Current != '\n' )
		{
			sb.Append( Current );
			Advance();
		}
		
		return new UsdaToken( UsdaTokenType.Comment, sb.ToString(), startLine, startColumn );
	}
	
	private UsdaToken ReadNumber( int startLine, int startColumn )
	{
		var sb = new StringBuilder();
		var isFloat = false;
		
		// Handle sign
		if ( Current == '-' || Current == '+' )
		{
			sb.Append( Current );
			Advance();
		}
		
		// Read digits before decimal
		while ( _position < _source.Length && char.IsDigit( Current ) )
		{
			sb.Append( Current );
			Advance();
		}
		
		// Decimal point
		if ( _position < _source.Length && Current == '.' )
		{
			isFloat = true;
			sb.Append( Current );
			Advance();
			
			// Read digits after decimal
			while ( _position < _source.Length && char.IsDigit( Current ) )
			{
				sb.Append( Current );
				Advance();
			}
		}
		
		// Exponent (e or E)
		if ( _position < _source.Length && (Current == 'e' || Current == 'E') )
		{
			isFloat = true;
			sb.Append( Current );
			Advance();
			
			// Optional sign after exponent
			if ( _position < _source.Length && (Current == '+' || Current == '-') )
			{
				sb.Append( Current );
				Advance();
			}
			
			// Exponent digits
			while ( _position < _source.Length && char.IsDigit( Current ) )
			{
				sb.Append( Current );
				Advance();
			}
		}
		
		var type = isFloat ? UsdaTokenType.Float : UsdaTokenType.Integer;
		return new UsdaToken( type, sb.ToString(), startLine, startColumn );
	}
	
	private UsdaToken ReadIdentifier( int startLine, int startColumn )
	{
		var sb = new StringBuilder();
		
		while ( _position < _source.Length && (char.IsLetterOrDigit( Current ) || Current == '_') )
		{
			sb.Append( Current );
			Advance();
		}
		
		return new UsdaToken( UsdaTokenType.Identifier, sb.ToString(), startLine, startColumn );
	}
	
	private void SkipWhitespace()
	{
		while ( _position < _source.Length )
		{
			var c = Current;
			if ( c == ' ' || c == '\t' || c == '\r' )
			{
				Advance();
			}
			else if ( c == '\n' )
			{
				Advance();
				_line++;
				_column = 1;
			}
			else
			{
				break;
			}
		}
	}
	
	private char Current => _source[_position];
	
	private void Advance()
	{
		if ( _position < _source.Length )
		{
			_position++;
			_column++;
		}
	}
}

