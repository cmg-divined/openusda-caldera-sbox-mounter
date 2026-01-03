using System;
using System.Collections.Generic;

namespace Sandbox.Usd.Schema;

/// <summary>
/// Represents a typed value in USD
/// </summary>
public abstract class UsdValue
{
	public abstract object GetValue();
}

public class UsdString : UsdValue
{
	public string Value { get; }
	public UsdString( string value ) => Value = value;
	public override object GetValue() => Value;
	public override string ToString() => $"\"{Value}\"";
}

public class UsdInt : UsdValue
{
	public int Value { get; }
	public UsdInt( int value ) => Value = value;
	public override object GetValue() => Value;
	public override string ToString() => Value.ToString();
}

public class UsdFloat : UsdValue
{
	public float Value { get; }
	public UsdFloat( float value ) => Value = value;
	public override object GetValue() => Value;
	public override string ToString() => Value.ToString();
}

public class UsdDouble : UsdValue
{
	public double Value { get; }
	public UsdDouble( double value ) => Value = value;
	public override object GetValue() => Value;
	public override string ToString() => Value.ToString();
}

public class UsdBool : UsdValue
{
	public bool Value { get; }
	public UsdBool( bool value ) => Value = value;
	public override object GetValue() => Value;
	public override string ToString() => Value.ToString().ToLower();
}

public class UsdToken : UsdValue
{
	public string Value { get; }
	public UsdToken( string value ) => Value = value;
	public override object GetValue() => Value;
	public override string ToString() => Value;
}

public class UsdAssetPath : UsdValue
{
	public string Path { get; }
	public UsdAssetPath( string path ) => Path = path;
	public override object GetValue() => Path;
	public override string ToString() => $"@{Path}@";
}

public class UsdVector2 : UsdValue
{
	public Vector2 Value { get; }
	public UsdVector2( float x, float y ) => Value = new Vector2( x, y );
	public UsdVector2( Vector2 value ) => Value = value;
	public override object GetValue() => Value;
	public override string ToString() => $"({Value.x}, {Value.y})";
}

public class UsdVector3 : UsdValue
{
	public Vector3 Value { get; }
	public UsdVector3( float x, float y, float z ) => Value = new Vector3( x, y, z );
	public UsdVector3( Vector3 value ) => Value = value;
	public override object GetValue() => Value;
	public override string ToString() => $"({Value.x}, {Value.y}, {Value.z})";
}

public class UsdVector4 : UsdValue
{
	public Vector4 Value { get; }
	public UsdVector4( float x, float y, float z, float w ) => Value = new Vector4( x, y, z, w );
	public UsdVector4( Vector4 value ) => Value = value;
	public override object GetValue() => Value;
	public override string ToString() => $"({Value.x}, {Value.y}, {Value.z}, {Value.w})";
}

public class UsdMatrix4 : UsdValue
{
	public Matrix Value { get; }
	public UsdMatrix4( Matrix value ) => Value = value;
	public override object GetValue() => Value;
}

public class UsdArray<T> : UsdValue where T : UsdValue
{
	public List<T> Values { get; } = new();
	public override object GetValue() => Values;
	public int Count => Values.Count;
	public T this[int index] => Values[index];
	public void Add( T value ) => Values.Add( value );
}

// Specialized arrays for common types
public class UsdIntArray : UsdValue
{
	public List<int> Values { get; } = new();
	public override object GetValue() => Values;
	public int Count => Values.Count;
	public int this[int index] => Values[index];
}

public class UsdFloatArray : UsdValue
{
	public List<float> Values { get; } = new();
	public override object GetValue() => Values;
	public int Count => Values.Count;
	public float this[int index] => Values[index];
}

public class UsdVector2Array : UsdValue
{
	public List<Vector2> Values { get; } = new();
	public override object GetValue() => Values;
	public int Count => Values.Count;
	public Vector2 this[int index] => Values[index];
}

public class UsdVector3Array : UsdValue
{
	public List<Vector3> Values { get; } = new();
	public override object GetValue() => Values;
	public int Count => Values.Count;
	public Vector3 this[int index] => Values[index];
}

public class UsdStringArray : UsdValue
{
	public List<string> Values { get; } = new();
	public override object GetValue() => Values;
	public int Count => Values.Count;
	public string this[int index] => Values[index];
}

public class UsdTokenArray : UsdValue
{
	public List<string> Values { get; } = new();
	public override object GetValue() => Values;
	public int Count => Values.Count;
	public string this[int index] => Values[index];
}

/// <summary>
/// Represents a USD relationship (connection to other prims)
/// </summary>
public class UsdRelationship
{
	public string Name { get; set; }
	public List<string> Targets { get; } = new();
}

/// <summary>
/// Array of 4x4 matrices for skeleton transforms
/// </summary>
public class UsdMatrix4Array : UsdValue
{
	public List<Matrix> Values { get; } = new();
	public override object GetValue() => Values;
	public int Count => Values.Count;
	public Matrix this[int index] => Values[index];
}

