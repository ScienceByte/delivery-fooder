using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;

[Tool]
public partial class LooseItemExporter : EditorScript
{
	private const string OutputPath = "res://Shared/LooseItemData.generated.cs";
	private const string IncludeGroupName = "loose_item_export";
	private const string ExcludeGroupName = "loose_item_ignore";
	private const string IncludeMetaName = "loose_item_export";
	private const string ExcludeMetaName = "loose_item_ignore";

	public override void _Run()
	{
		var sceneRoot = EditorInterface.Singleton.GetEditedSceneRoot();
		if (sceneRoot == null)
		{
			GD.PushError("No edited scene root found.");
			return;
		}

		var itemRoots = new List<Node3D>();
		CollectLooseItemRoots(sceneRoot, itemRoots);
		if (itemRoots.Count == 0)
		{
			GD.PushError(
				$"No loose item roots found. Tag the root Node3D of each loose item with the '{IncludeGroupName}' group or '{IncludeMetaName}' metadata."
			);
			return;
		}

		var items = new List<LooseItemDefinition>(itemRoots.Count);
		foreach (var itemRoot in itemRoots)
		{
			if (TryBuildDefinition(itemRoot, out var item))
			{
				items.Add(item);
			}
		}

		if (items.Count == 0)
		{
			GD.PushError("Tagged loose items were found, but none contained mesh geometry to export.");
			return;
		}

		items.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
		var output = BuildGeneratedFile(items);
		var absolutePath = ProjectSettings.GlobalizePath(OutputPath);
		Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
		File.WriteAllText(absolutePath, output, Encoding.UTF8);
		GD.Print($"Loose item data exported to {OutputPath}");
	}

	private static void CollectLooseItemRoots(Node node, List<Node3D> itemRoots)
	{
		if (node is Node3D node3D)
		{
			if (IsExplicitlyExcluded(node3D))
			{
				return;
			}

			if (IsExplicitlyIncluded(node3D))
			{
				CollectExportEntriesFromIncludedRoot(node3D, itemRoots);
				return;
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				CollectLooseItemRoots(childNode, itemRoots);
			}
		}
	}

	private static void CollectExportEntriesFromIncludedRoot(Node3D includedRoot, List<Node3D> itemRoots)
	{
		var addedAnyChildren = false;
		foreach (var child in includedRoot.GetChildren())
		{
			if (child is not Node3D childNode3D || IsExplicitlyExcluded(childNode3D))
			{
				continue;
			}

			if (!ContainsMeshGeometry(childNode3D))
			{
				continue;
			}

			itemRoots.Add(childNode3D);
			addedAnyChildren = true;
		}

		if (!addedAnyChildren && ContainsMeshGeometry(includedRoot))
		{
			itemRoots.Add(includedRoot);
		}
	}

	private static bool TryBuildDefinition(Node3D itemRoot, out LooseItemDefinition item)
	{
		var meshes = new List<MeshInstance3D>();
		CollectMeshNodes(itemRoot, meshes);
		if (meshes.Count == 0)
		{
			item = default;
			return false;
		}

		var rootInverse = itemRoot.GlobalTransform.AffineInverse();
		var hasBounds = false;
		var min = Vector3.Zero;
		var max = Vector3.Zero;

		foreach (var meshNode in meshes)
		{
			if (meshNode.Mesh == null)
			{
				continue;
			}

			foreach (var localPoint in GetRootLocalAabbCorners(meshNode, rootInverse))
			{
				if (!hasBounds)
				{
					min = localPoint;
					max = localPoint;
					hasBounds = true;
					continue;
				}

				min = new Vector3(
					MathF.Min(min.X, localPoint.X),
					MathF.Min(min.Y, localPoint.Y),
					MathF.Min(min.Z, localPoint.Z)
				);
				max = new Vector3(
					MathF.Max(max.X, localPoint.X),
					MathF.Max(max.Y, localPoint.Y),
					MathF.Max(max.Z, localPoint.Z)
				);
			}
		}

		if (!hasBounds)
		{
			item = default;
			return false;
		}

		item = new LooseItemDefinition(
			itemRoot.Name,
			itemRoot.GlobalPosition.X,
			itemRoot.GlobalPosition.Y,
			itemRoot.GlobalPosition.Z,
			itemRoot.GlobalRotationDegrees.X,
			itemRoot.GlobalRotationDegrees.Y,
			itemRoot.GlobalRotationDegrees.Z,
			itemRoot.GlobalBasis.Scale.X,
			itemRoot.GlobalBasis.Scale.Y,
			itemRoot.GlobalBasis.Scale.Z,
			MathF.Max(0.05f, MathF.Max(MathF.Abs(min.X), MathF.Abs(max.X))),
			MathF.Max(0.05f, MathF.Max(MathF.Abs(min.Z), MathF.Abs(max.Z))),
			min.Y,
			MathF.Max(min.Y + 0.04f, max.Y)
		);
		return true;
	}

	private static void CollectMeshNodes(Node node, List<MeshInstance3D> meshes)
	{
		if (node is MeshInstance3D meshNode)
		{
			meshes.Add(meshNode);
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				CollectMeshNodes(childNode, meshes);
			}
		}
	}

	private static bool ContainsMeshGeometry(Node node)
	{
		if (node is MeshInstance3D meshNode && meshNode.Mesh != null)
		{
			return true;
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode && ContainsMeshGeometry(childNode))
			{
				return true;
			}
		}

		return false;
	}

	private static IEnumerable<Vector3> GetRootLocalAabbCorners(MeshInstance3D meshNode, Transform3D rootInverse)
	{
		var meshAabb = meshNode.Mesh.GetAabb();
		var meshTransform = meshNode.GlobalTransform;
		var min = meshAabb.Position;
		var max = meshAabb.End;

		yield return rootInverse * (meshTransform * new Vector3(min.X, min.Y, min.Z));
		yield return rootInverse * (meshTransform * new Vector3(max.X, min.Y, min.Z));
		yield return rootInverse * (meshTransform * new Vector3(min.X, max.Y, min.Z));
		yield return rootInverse * (meshTransform * new Vector3(max.X, max.Y, min.Z));
		yield return rootInverse * (meshTransform * new Vector3(min.X, min.Y, max.Z));
		yield return rootInverse * (meshTransform * new Vector3(max.X, min.Y, max.Z));
		yield return rootInverse * (meshTransform * new Vector3(min.X, max.Y, max.Z));
		yield return rootInverse * (meshTransform * new Vector3(max.X, max.Y, max.Z));
	}

	private static bool IsExplicitlyIncluded(Node node)
		=> node.IsInGroup(IncludeGroupName) || HasTrueMeta(node, IncludeMetaName);

	private static bool IsExplicitlyExcluded(Node node)
		=> node.IsInGroup(ExcludeGroupName) || HasTrueMeta(node, ExcludeMetaName);

	private static bool HasTrueMeta(Node node, string metaName)
	{
		if (!node.HasMeta(metaName))
		{
			return false;
		}

		var value = node.GetMeta(metaName);
		return value.VariantType == Variant.Type.Bool && value.AsBool();
	}

	private static string BuildGeneratedFile(List<LooseItemDefinition> items)
	{
		var builder = new StringBuilder();
		builder.AppendLine("// Auto-generated by LooseItemExporter.cs");
		builder.AppendLine("public static partial class LooseItemData");
		builder.AppendLine("{");
		builder.AppendLine("    public static readonly LooseItemDefinition[] Items =");
		builder.AppendLine("    [");

		for (var index = 0; index < items.Count; index++)
		{
			var item = items[index];
			builder.Append("        new LooseItemDefinition(");
			builder.Append($"\"{Escape(item.Name)}\", ");
			builder.Append($"{Format(item.PositionX)}f, {Format(item.PositionY)}f, {Format(item.PositionZ)}f, ");
			builder.Append($"{Format(item.RotationX)}f, {Format(item.RotationY)}f, {Format(item.RotationZ)}f, ");
			builder.Append($"{Format(item.ScaleX)}f, {Format(item.ScaleY)}f, {Format(item.ScaleZ)}f, ");
			builder.Append($"{Format(item.HalfWidth)}f, {Format(item.HalfDepth)}f, ");
			builder.Append($"{Format(item.MinY)}f, {Format(item.MaxY)}f)");
			if (index < items.Count - 1)
			{
				builder.Append(",");
			}

			builder.AppendLine();
		}

		builder.AppendLine("    ];");
		builder.AppendLine("}");
		return builder.ToString();
	}

	private static string Format(float value)
		=> value.ToString("0.######", CultureInfo.InvariantCulture);

	private static string Escape(string value)
		=> value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
