using Godot;

public partial class LooseItemController : Node3D
{
	private readonly LooseItemDefinition _definition;
	private readonly Node3D _prototype;
	private StaticBody3D _collisionBody;

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
		AddCollisionBody();

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

	private void AddCollisionBody()
	{
		var halfWidth = _definition.HalfWidth;
		var halfDepth = _definition.HalfDepth;
		var minY = _definition.MinY;
		var maxY = _definition.MaxY;

		if (TryGetManualCollider(_definition.Name, out var manualHalfWidth, out var manualHalfDepth, out var manualMinY, out var manualMaxY))
		{
			halfWidth = manualHalfWidth;
			halfDepth = manualHalfDepth;
			minY = manualMinY;
			maxY = manualMaxY;
		}

		var size = new Vector3(
			Mathf.Max(0.1f, halfWidth * 2f),
			Mathf.Max(0.1f, maxY - minY),
			Mathf.Max(0.1f, halfDepth * 2f)
		);
		var centerY = (minY + maxY) * 0.5f;

		_collisionBody = new StaticBody3D
		{
			Name = "CollisionBody",
		};
		_collisionBody.AddChild(new CollisionShape3D
		{
			Name = "CollisionShape",
			Shape = new BoxShape3D
			{
				Size = size,
			},
			Position = new Vector3(0f, centerY, 0f),
		});
		AddChild(_collisionBody);
	}

	private static bool TryGetManualCollider(
		string itemName,
		out float halfWidth,
		out float halfDepth,
		out float minY,
		out float maxY
	)
	{
		switch (itemName)
		{
			case "Firetruck":
				halfWidth = 1.05f;
				halfDepth = 1.95f;
				minY = -0.05f;
				maxY = 1.85f;
				return true;
			case "GarbageTruck":
				halfWidth = 1.05f;
				halfDepth = 2.05f;
				minY = -0.05f;
				maxY = 1.8f;
				return true;
			case "RaceFuture":
			case "RaceFuture2":
				halfWidth = 0.9f;
				halfDepth = 1.55f;
				minY = -0.05f;
				maxY = 1.0f;
				return true;
			case "WheelDefault":
			case "WheelDefault2":
			case "WheelDefault3":
				halfWidth = 0.42f;
				halfDepth = 0.42f;
				minY = -0.35f;
				maxY = 0.35f;
				return true;
			default:
				halfWidth = 0f;
				halfDepth = 0f;
				minY = 0f;
				maxY = 0f;
				return false;
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
}
