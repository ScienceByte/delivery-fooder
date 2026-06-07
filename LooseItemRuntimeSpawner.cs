using System.Collections.Generic;
using Godot;

public partial class LooseItemRuntimeSpawner : Node3D
{
	private const string ExportGroupName = "loose_item_export";

	public override void _Ready()
	{
		var prototypes = CollectPrototypes();
		HidePrototypeVisuals(prototypes.Values);

		foreach (var item in LooseItemData.Items)
		{
			prototypes.TryGetValue(item.Name, out var prototype);
			AddChild(new LooseItemController(item, prototype));
		}
	}

	private Dictionary<string, Node3D> CollectPrototypes()
	{
		var prototypes = new Dictionary<string, Node3D>(System.StringComparer.Ordinal);
		foreach (var node in GetTree().GetNodesInGroup(ExportGroupName))
		{
			if (node is not Node3D node3D)
			{
				continue;
			}

			foreach (var prototype in ExpandPrototypeRoots(node3D))
			{
				if (!prototypes.ContainsKey(prototype.Name))
				{
					prototypes.Add(prototype.Name, prototype);
				}
			}
		}

		return prototypes;
	}

	private static IEnumerable<Node3D> ExpandPrototypeRoots(Node3D taggedRoot)
	{
		var yieldedAnyChildren = false;
		foreach (var child in taggedRoot.GetChildren())
		{
			if (child is not Node3D childNode3D)
			{
				continue;
			}

			if (!ContainsVisibleMeshGeometry(childNode3D))
			{
				continue;
			}

			yield return childNode3D;
			yieldedAnyChildren = true;
		}

		if (!yieldedAnyChildren && ContainsVisibleMeshGeometry(taggedRoot))
		{
			yield return taggedRoot;
		}
	}

	private static void HidePrototypeVisuals(IEnumerable<Node3D> prototypes)
	{
		foreach (var prototype in prototypes)
		{
			SetVisualsVisible(prototype, false);
		}
	}

	private static void SetVisualsVisible(Node node, bool visible)
	{
		if (node is VisualInstance3D visual)
		{
			visual.Visible = visible;
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				SetVisualsVisible(childNode, visible);
			}
		}
	}

	private static bool ContainsVisibleMeshGeometry(Node node)
	{
		if (node is MeshInstance3D meshNode && meshNode.Mesh != null)
		{
			return true;
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode && ContainsVisibleMeshGeometry(childNode))
			{
				return true;
			}
		}

		return false;
	}
}
