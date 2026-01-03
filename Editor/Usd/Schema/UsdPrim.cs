using System.Collections.Generic;

namespace Sandbox.Usd.Schema;

/// <summary>
/// Base class for all USD primitives (prims)
/// </summary>
public class UsdPrim
{
	/// <summary>
	/// The prim's name (last component of path)
	/// </summary>
	public string Name { get; set; }
	
	/// <summary>
	/// Full path in the stage hierarchy (e.g., /world/building/room)
	/// </summary>
	public string Path { get; set; }
	
	/// <summary>
	/// The prim's type (Xform, Mesh, Scope, etc.)
	/// </summary>
	public string TypeName { get; set; }
	
	/// <summary>
	/// Specifier: def, over, class
	/// </summary>
	public string Specifier { get; set; } = "def";
	
	/// <summary>
	/// Parent prim (null for root prims)
	/// </summary>
	public UsdPrim Parent { get; set; }
	
	/// <summary>
	/// Child prims
	/// </summary>
	public List<UsdPrim> Children { get; } = new();
	
	/// <summary>
	/// Prim attributes (properties with values)
	/// </summary>
	public Dictionary<string, UsdValue> Attributes { get; } = new();
	
	/// <summary>
	/// Prim metadata (things like kind, instanceable, etc.)
	/// </summary>
	public Dictionary<string, UsdValue> Metadata { get; } = new();
	
	/// <summary>
	/// References to other USD files
	/// </summary>
	public List<string> References { get; } = new();
	
	/// <summary>
	/// Payloads (lazy-loaded references)
	/// </summary>
	public List<string> Payloads { get; } = new();
	
	/// <summary>
	/// Inherits paths
	/// </summary>
	public List<string> Inherits { get; } = new();
	
	/// <summary>
	/// Variant sets defined on this prim
	/// </summary>
	public Dictionary<string, Dictionary<string, UsdPrim>> VariantSets { get; } = new();
	
	/// <summary>
	/// Currently selected variants
	/// </summary>
	public Dictionary<string, string> Variants { get; } = new();
	
	/// <summary>
	/// API schemas applied to this prim
	/// </summary>
	public List<string> ApiSchemas { get; } = new();
	
	/// <summary>
	/// Relationships (connections to other prims)
	/// </summary>
	public List<UsdRelationship> Relationships { get; } = new();
	
	/// <summary>
	/// Custom data dictionary
	/// </summary>
	public Dictionary<string, object> CustomData { get; } = new();
	
	/// <summary>
	/// Get a typed attribute value
	/// </summary>
	public T GetAttribute<T>( string name ) where T : UsdValue
	{
		if ( Attributes.TryGetValue( name, out var value ) && value is T typed )
			return typed;
		return null;
	}
	
	/// <summary>
	/// Check if this prim has a specific attribute
	/// </summary>
	public bool HasAttribute( string name ) => Attributes.ContainsKey( name );
	
	/// <summary>
	/// Get computed local transform from xformOps
	/// Converts from USD coordinate system (Y-forward, X-right, Z-up) 
	/// to s&box coordinate system (X-forward, Y-right, Z-up)
	/// </summary>
	public Transform GetLocalTransform()
	{
		// Check for direct transform matrix
		if ( Attributes.TryGetValue( "xformOp:transform", out var transformValue ) && transformValue is UsdMatrix4 matrix )
		{
			return MatrixToTransform( matrix.Value );
		}
		
		var position = Vector3.Zero;
		var rotation = Rotation.Identity;
		var scale = Vector3.One;
		
		// Get xformOpOrder to know which ops to apply
		if ( !Attributes.TryGetValue( "xformOpOrder", out var orderValue ) )
			return new Transform( position, rotation, scale );
		
		var order = orderValue as UsdTokenArray;
		if ( order == null )
			return new Transform( position, rotation, scale );
		
		// Apply transforms in order
		foreach ( var opName in order.Values )
		{
			if ( !Attributes.TryGetValue( opName, out var opValue ) )
				continue;
			
			if ( opName.StartsWith( "xformOp:translate" ) && opValue is UsdVector3 translate )
			{
				// Convert position: USD (x, y, z) -> s&box (y, -x, z)
				// Negating X converts reflection to rotation (preserves handedness)
				var usdPos = translate.Value;
				position = new Vector3( usdPos.y, -usdPos.x, usdPos.z );
			}
			else if ( opName.StartsWith( "xformOp:rotateXYZ" ) && opValue is UsdVector3 rotateXYZ )
			{
				// USD rotateXYZ uses intrinsic XYZ convention (rotate around local axes in order X→Y→Z)
				// This is equivalent to extrinsic ZYX: R = Rz * Ry * Rx
				// Build the rotation matrix, extract forward/up, convert to s&box - same as MatrixToTransform
				var angles = rotateXYZ.Value;
				rotation = EulerXYZToRotation( angles.x, angles.y, angles.z );
			}
			else if ( opName.StartsWith( "xformOp:rotateX" ) && opValue is UsdFloat rotX )
			{
				// USD X → s&box -Y, so negate the angle
				rotation *= Rotation.FromAxis( Vector3.Right, -rotX.Value );
			}
			else if ( opName.StartsWith( "xformOp:rotateY" ) && opValue is UsdFloat rotY )
			{
				// USD Y → s&box X, same direction
				rotation *= Rotation.FromAxis( Vector3.Forward, rotY.Value );
			}
			else if ( opName.StartsWith( "xformOp:rotateZ" ) && opValue is UsdFloat rotZ )
			{
				// USD Z → s&box Z, same direction
				rotation *= Rotation.FromAxis( Vector3.Up, rotZ.Value );
			}
			else if ( opName.StartsWith( "xformOp:scale" ) && opValue is UsdVector3 scaleVec )
			{
				// Convert scale: USD (x, y, z) -> s&box (y, x, z)
				var usdScale = scaleVec.Value;
				scale = new Vector3( usdScale.y, usdScale.x, usdScale.z );
			}
			else if ( opName.StartsWith( "xformOp:transform" ) && opValue is UsdMatrix4 mat )
			{
				return MatrixToTransform( mat.Value );
			}
		}
		
		return new Transform( position, rotation, scale );
	}
	
	/// <summary>
	/// Matrix -> Transform with USD (Y-forward) to s&box (X-forward) conversion
	/// </summary>
	private static Transform MatrixToTransform( Matrix matrix )
	{
		// Extract translation from the matrix (last row) and convert coordinates
		// USD (x, y, z) -> s&box (y, -x, z) - negating X to convert reflection to rotation
		var position = new Vector3( matrix.M42, -matrix.M41, matrix.M43 );
		
		// Extract scale from the matrix (length of basis vectors)
		// USD X-axis -> s&box Y-axis, USD Y-axis -> s&box X-axis
		var usdScaleX = new Vector3( matrix.M11, matrix.M12, matrix.M13 ).Length;
		var usdScaleY = new Vector3( matrix.M21, matrix.M22, matrix.M23 ).Length;
		var usdScaleZ = new Vector3( matrix.M31, matrix.M32, matrix.M33 ).Length;
		var scale = new Vector3( usdScaleY, usdScaleX, usdScaleZ );
		
		// Prevent division by zero
		if ( usdScaleX < 0.0001f ) usdScaleX = 1f;
		if ( usdScaleY < 0.0001f ) usdScaleY = 1f;
		if ( usdScaleZ < 0.0001f ) usdScaleZ = 1f;
		
		// Extract rotation by getting forward and up vectors from the matrix
		// USD matrix rows: Row1=X-axis (right), Row2=Y-axis (forward), Row3=Z-axis (up)
		var usdForward = new Vector3( matrix.M21 / usdScaleY, matrix.M22 / usdScaleY, matrix.M23 / usdScaleY );
		var usdUp = new Vector3( matrix.M31 / usdScaleZ, matrix.M32 / usdScaleZ, matrix.M33 / usdScaleZ );
		
		// Convert basis vectors from USD to s&box coordinates
		// Swap X and Y, and negate Y component (which was USD X) to convert reflection to rotation
		var sboxForward = new Vector3( usdForward.y, -usdForward.x, usdForward.z );
		var sboxUp = new Vector3( usdUp.y, -usdUp.x, usdUp.z );
		
		var rotation = Rotation.LookAt( sboxForward.Normal, sboxUp.Normal );
		
		return new Transform( position, rotation, scale );
	}
	
	/// <summary>
	/// USD intrinsic XYZ Euler -> s&box Rotation
	/// Intrinsic XYZ = extrinsic ZYX, so R = Rz * Ry * Rx
	/// </summary>
	private static Rotation EulerXYZToRotation( float xDeg, float yDeg, float zDeg )
	{
		// Convert degrees to radians
		const float DEG2RAD = (float)(System.Math.PI / 180.0);
		float xRad = xDeg * DEG2RAD;
		float yRad = yDeg * DEG2RAD;
		float zRad = zDeg * DEG2RAD;
		
		// Precompute sin/cos
		float cx = (float)System.Math.Cos( xRad );
		float sx = (float)System.Math.Sin( xRad );
		float cy = (float)System.Math.Cos( yRad );
		float sy = (float)System.Math.Sin( yRad );
		float cz = (float)System.Math.Cos( zRad );
		float sz = (float)System.Math.Sin( zRad );
		
		// Build rotation matrix R = Rz * Ry * Rx (intrinsic XYZ = extrinsic ZYX)
		// Row 0 (X-axis/right): (cy*cz, cy*sz, -sy)
		// Row 1 (Y-axis/forward): (cz*sy*sx - sz*cx, sz*sy*sx + cz*cx, cy*sx)
		// Row 2 (Z-axis/up): (cz*sy*cx + sz*sx, sz*sy*cx - cz*sx, cy*cx)
		
		// Extract forward (USD Y-axis = row 1) and up (USD Z-axis = row 2)
		var usdForward = new Vector3(
			cz * sy * sx - sz * cx,
			sz * sy * sx + cz * cx,
			cy * sx
		);
		var usdUp = new Vector3(
			cz * sy * cx + sz * sx,
			sz * sy * cx - cz * sx,
			cy * cx
		);
		
		// Convert from USD coordinates to s&box coordinates: (x, y, z) -> (y, -x, z)
		var sboxForward = new Vector3( usdForward.y, -usdForward.x, usdForward.z );
		var sboxUp = new Vector3( usdUp.y, -usdUp.x, usdUp.z );
		
		return Rotation.LookAt( sboxForward.Normal, sboxUp.Normal );
	}
	
	/// <summary>
	/// Add a child prim
	/// </summary>
	public void AddChild( UsdPrim child )
	{
		child.Parent = this;
		child.Path = string.IsNullOrEmpty( Path ) ? $"/{child.Name}" : $"{Path}/{child.Name}";
		Children.Add( child );
	}
	
	public override string ToString() => $"{Specifier} {TypeName} \"{Name}\"";
}

