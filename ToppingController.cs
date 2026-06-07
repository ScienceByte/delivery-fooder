using System;
using Godot;
using SpacetimeDB.Types;

public partial class ToppingController : Node3D
{
	private const float PositionInterpolationSpeed = 14f;
	private const float RotationInterpolationSpeed = 12f;
	private const float ToppingYawOffsetRadians = Mathf.Pi * 0.5f;
	private const string PrototypeRootName = "ToppingProfilesSource";

	private readonly int _toppingId;
	private Vector3 _targetPosition;
	private Vector3 _targetRotation;
	private Vector3 _targetScale = Vector3.One;
	private MeshInstance3D _mesh;
	private Node3D _visualInstance;
	private Vector3 _lastVelocity;

	public string ToppingName { get; private set; }
	public ToppingState State { get; private set; }

	public ToppingController(Topping topping)
	{
		_toppingId = topping.ToppingId;
		_targetPosition = topping.Position;
		Position = _targetPosition;
		ToppingName = topping.Name;
		State = topping.State;
		_lastVelocity = topping.Velocity;
	}

	public override void _Ready()
	{
		Name = $"Topping - {ToppingName}";
		_mesh = new MeshInstance3D
		{
			Name = "PlaceholderMesh",
			Mesh = new BoxMesh { Size = new Vector3(2.7f, 0.2f, 2.7f) },
		};
		AddChild(_mesh);
		UpdateVisual();
	}

	public override void _Process(double delta)
	{
		UpdateDesiredTransformTargets();

		var positionWeight = 1f - Mathf.Exp(-PositionInterpolationSpeed * (float)delta);
		var rotationWeight = 1f - Mathf.Exp(-RotationInterpolationSpeed * (float)delta);
		Position = Position.Lerp(_targetPosition, positionWeight);
		Rotation = new Vector3(
			Mathf.LerpAngle(Rotation.X, _targetRotation.X, rotationWeight),
			Mathf.LerpAngle(Rotation.Y, _targetRotation.Y, rotationWeight),
			Mathf.LerpAngle(Rotation.Z, _targetRotation.Z, rotationWeight)
		);
		Scale = Scale.Lerp(_targetScale, rotationWeight);
	}

	public void ApplyNetworkState(Topping topping)
	{
		if (topping.ToppingId != _toppingId)
		{
			GD.PushError($"Cannot apply topping {topping.ToppingId} state to topping {_toppingId} controller.");
			return;
		}

		ToppingName = topping.Name;
		State = topping.State;
		_targetPosition = topping.Position;
		_lastVelocity = topping.Velocity;
		Name = $"Topping - {ToppingName}";
		UpdateVisual();
	}

	private void UpdateVisual()
	{
		if (_mesh == null)
		{
			return;
		}

		EnsureVisualInstance();

		var isVisible = State != ToppingState.WaitingAtSummit;
		_mesh.Visible = _visualInstance == null && isVisible;
		_mesh.MaterialOverride = new StandardMaterial3D
		{
			AlbedoColor = ResolveFallbackColor(ToppingName),
		};

		if (_visualInstance != null)
		{
			_visualInstance.Visible = isVisible;
		}
	}

	private void UpdateDesiredTransformTargets()
	{
		_targetScale = Vector3.One;

		if (State is ToppingState.Attached or ToppingState.Placed)
		{
			var sandwich = FindSandwich();
			if (sandwich != null)
			{
				_targetRotation = sandwich.Rotation;
				if (IsBread(ToppingName))
				{
					_targetScale = ResolveBreadBridgeScale();
				}

				ApplyVisualOrientationOverrides();
				return;
			}
		}

		_targetRotation = ResolveFreeRotation();
		ApplyVisualOrientationOverrides();
	}

	private void EnsureVisualInstance()
	{
		if (_visualInstance != null && string.Equals(_visualInstance.Name, ToppingName, StringComparison.Ordinal))
		{
			return;
		}

		if (_visualInstance != null)
		{
			_visualInstance.QueueFree();
			_visualInstance = null;
		}

		var prototype = FindPrototype(ToppingName);
		if (prototype == null)
		{
			return;
		}

		_visualInstance = prototype.Duplicate() as Node3D;
		if (_visualInstance == null)
		{
			return;
		}

		_visualInstance.Name = ToppingName;
		_visualInstance.Position = Vector3.Zero;
		AddChild(_visualInstance);
		ApplyVisualOrientationOverrides();
	}

	private void ApplyVisualOrientationOverrides()
	{
		if (_visualInstance == null)
		{
			return;
		}

		_visualInstance.Position = Vector3.Zero;
		_visualInstance.Rotation = ResolvePrototypeRotationOffset();
	}

	private Vector3 ResolvePrototypeRotationOffset()
	{
		if (!IsBread(ToppingName))
		{
			return new Vector3(0f, ToppingYawOffsetRadians, 0f);
		}

		var profile = ToppingShapeData.GetProfile(ToppingName);
		return profile.HalfWidth >= profile.HalfDepth
			? new Vector3(0f, ToppingYawOffsetRadians, 0f)
			: Vector3.Zero;
	}

	private Vector3 ResolveBreadBridgeScale()
	{
		var players = FindPlayers();
		if (players.Length < 2)
		{
			return Vector3.One;
		}

		var span = players[0].HeadSupportPoint.DistanceTo(players[1].HeadSupportPoint);
		var baseSpan = players[0].AttachmentOffset.DistanceTo(players[1].AttachmentOffset);
		if (baseSpan <= 0.001f)
		{
			return Vector3.One;
		}

		var stretch = Mathf.Clamp(span / baseSpan, 0.9f, 1.15f);
		return new Vector3(1f, 1f, stretch);
	}

	private Vector3 ResolveFreeRotation()
	{
		var horizontalVelocity = new Vector2(_lastVelocity.X, _lastVelocity.Z);
		if (horizontalVelocity.LengthSquared() < 0.0001f)
		{
			return _targetRotation;
		}

		var yaw = Mathf.Atan2(_lastVelocity.X, _lastVelocity.Z);
		var pitch = Mathf.Clamp(-_lastVelocity.Z * 0.025f, -0.35f, 0.35f);
		var roll = Mathf.Clamp(_lastVelocity.X * 0.025f, -0.35f, 0.35f);
		return new Vector3(pitch, yaw, roll);
	}

	private SandwichController FindSandwich()
	{
		foreach (var node in GetTree().GetNodesInGroup(SandwichController.GroupName))
		{
			if (node is SandwichController sandwich)
			{
				return sandwich;
			}
		}

		return null;
	}

	private PlayerController3D[] FindPlayers()
	{
		var players = new System.Collections.Generic.List<PlayerController3D>(2);
		foreach (var node in GetTree().GetNodesInGroup(PlayerController3D.GroupName))
		{
			if (node is PlayerController3D player)
			{
				players.Add(player);
			}
		}

		players.Sort(static (left, right) => left.PlayerId.CompareTo(right.PlayerId));
		return players.ToArray();
	}

	private Node3D FindPrototype(string toppingName)
	{
		var sceneRoot = GetTree()?.CurrentScene;
		if (sceneRoot == null)
		{
			return null;
		}

		var prototypeRoot = sceneRoot.FindChild(PrototypeRootName, true, false);
		if (prototypeRoot == null)
		{
			return null;
		}

		var targetName = CanonicalizeName(toppingName);
		return FindPrototypeRecursive(prototypeRoot, targetName);
	}

	private static Node3D FindPrototypeRecursive(Node root, string targetName)
	{
		foreach (var child in root.GetChildren())
		{
			if (child is Node3D childNode3D)
			{
				if (CanonicalizeName(childNode3D.Name) == targetName)
				{
					return childNode3D;
				}

				var nested = FindPrototypeRecursive(childNode3D, targetName);
				if (nested != null)
				{
					return nested;
				}
			}
			else if (child is Node childNode)
			{
				var nested = FindPrototypeRecursive(childNode, targetName);
				if (nested != null)
				{
					return nested;
				}
			}
		}

		return null;
	}

	private static Color ResolveFallbackColor(string toppingName)
	{
		return CanonicalizeName(toppingName) switch
		{
			"lettuce" => Colors.LimeGreen,
			"tomato" => Colors.Tomato,
			"cheese" => Colors.Gold,
			"bacon" => new Color("8b4513"),
			"topbread" => new Color("deb887"),
			"bottombread" => Colors.Peru,
			_ => Colors.Wheat,
		};
	}

	private static string CanonicalizeName(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		var buffer = new System.Text.StringBuilder(value.Length);
		foreach (var character in value)
		{
			if (char.IsLetter(character))
			{
				buffer.Append(char.ToLowerInvariant(character));
			}
		}

		while (buffer.Length > 0 && char.IsDigit(buffer[^1]))
		{
			buffer.Length--;
		}

		return buffer.ToString();
	}

	private static bool IsBread(string toppingName)
	{
		var canonical = CanonicalizeName(toppingName);
		return canonical is "topbread" or "bottombread";
	}
}
