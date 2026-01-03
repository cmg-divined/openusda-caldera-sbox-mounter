using System.Collections.Generic;

namespace Sandbox.Usd.Schema;

/// <summary>
/// USD Skeleton primitive containing skeletal data for skinned meshes
/// </summary>
public class UsdSkeleton : UsdPrim
{
	public UsdSkeleton()
	{
		TypeName = "Skeleton";
	}
	
	/// <summary>
	/// Joint paths (hierarchical names like "tag_origin/bone1")
	/// </summary>
	public List<string> Joints
	{
		get
		{
			var attr = GetAttribute<UsdTokenArray>( "joints" );
			return attr?.Values ?? new List<string>();
		}
	}
	
	/// <summary>
	/// Joint display names
	/// </summary>
	public List<string> JointNames
	{
		get
		{
			var attr = GetAttribute<UsdTokenArray>( "jointNames" );
			return attr?.Values ?? new List<string>();
		}
	}
	
	/// <summary>
	/// Bind-pose transforms for each joint (world-space matrices at bind time)
	/// </summary>
	public List<Matrix> BindTransforms
	{
		get
		{
			var attr = GetAttribute<UsdMatrix4Array>( "bindTransforms" );
			return attr?.Values ?? new List<Matrix>();
		}
	}
	
	/// <summary>
	/// Rest transforms for each joint (local transforms relative to parent)
	/// </summary>
	public List<Matrix> RestTransforms
	{
		get
		{
			var attr = GetAttribute<UsdMatrix4Array>( "restTransforms" );
			return attr?.Values ?? new List<Matrix>();
		}
	}
}


