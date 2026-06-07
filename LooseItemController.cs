using Godot;

public partial class LooseItemController : Node3D
{
	private readonly LooseItemDefinition _definition;
	private readonly Node3D _prototype;

	public LooseItemController(LooseItemDefinition definition, Node3D prototype)
	{
		_definition = definition;
		_prototype = prototype;
	}

	public override void _Ready()
	{
		Name = $"LooseItem - {_definition.Name}";
		Position = new Vector3(_definition.PositionX, _definition.PositionY, _definition.PositionZ);
		RotationDegrees = new Vector3(_definition.RotationX, _definition.RotationY, _definition.RotationZ);
		Scale = new Vector3(_definition.ScaleX, _definition.ScaleY, _definition.ScaleZ);

		if (_prototype != null)
		{
			var visual = _prototype.Duplicate() as Node3D;
			if (visual != null)
			{
				visual.Name = $"{_definition.Name} Visual";
				visual.Position = Vector3.Zero;
				visual.Rotation = Vector3.Zero;
				visual.Scale = Vector3.One;
				SetVisualsVisible(visual, true);
				AddChild(visual);
				return;
			}
		}

		AddChild(new MeshInstance3D
		{
			Name = "PlaceholderMesh",
			Mesh = new BoxMesh
			{
				Size = new Vector3(
					Mathf.Max(0.1f, _definition.HalfWidth * 2f),
					Mathf.Max(0.1f, _definition.Thickness),
					Mathf.Max(0.1f, _definition.HalfDepth * 2f)
				),
			},
			Position = new Vector3(0f, (_definition.MinY + _definition.MaxY) * 0.5f, 0f),
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = Colors.SlateGray,
			},
		});
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
}
