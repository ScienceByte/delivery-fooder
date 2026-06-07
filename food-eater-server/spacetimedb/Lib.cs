using System;
using System.Linq;
using SpacetimeDB;

[SpacetimeDB.Type]
public enum ToppingState
{
    Attached,
    Dropped,
    WaitingAtSummit,
    Placed,
}

public static partial class Module
{
    private const float TickSeconds = 0.05f;
    private const int SandwichId = 0;
    private const float PlayerCarryRadius = 2.25f;
    private const float SandwichCarryHeight = 2.6f;
    private const float GroundedHeightTolerance = 0.05f;
    private const float DefaultSandwichSpeed = 5f;
    private const float DefaultGravity = 24f;
    private const float DefaultJumpImpulse = 8.5f;
    private const float MaxAirHeightBuffer = 10f;
    private const float JumpLiftInfluence = 0.85f;
    private const float JumpTiltFactor = 18f;
    private const float MovementPitchTorqueFactor = 42f;
    private const float MovementRollTorqueFactor = 42f;
    private const float JumpPitchTorqueFactor = 58f;
    private const float JumpRollTorqueFactor = 58f;
    private const float LoadPitchTorqueFactor = 22f;
    private const float LoadRollTorqueFactor = 22f;
    private const float AngularReturnSpring = 7.5f;
    private const float AngularVelocityDamping = 0.86f;
    private const float AngularImpactKick = 20f;
    private const float MaxSandwichAngle = 38f;
    private const float ToppingSlideAccelerationFactor = 0.45f;
    private const float ToppingAngularAccelerationFactor = 0.09f;
    private const float ToppingSlideBoundary = 1.1f;
    private const float ImpactSlideImpulse = 2.8f;
    private const uint InitialShuffleSeed = 0xA53C9E2Du;

    [Table(Accessor = "simulation_timer", Scheduled = nameof(Simulate), ScheduledAt = nameof(scheduled_at))]
    public partial struct SimulationTimer
    {
        [PrimaryKey, AutoInc]
        public ulong scheduled_id;
        public ScheduleAt scheduled_at;
    }

    [Table(Accessor = "player_motion")]
    public partial struct PlayerMotion
    {
        [PrimaryKey]
        public Identity identity;
        public float vertical_velocity;
    }

    [Table(Accessor = "sandwich_motion")]
    public partial struct SandwichMotion
    {
        [PrimaryKey]
        public int sandwich_id;
        public float pitch_velocity;
        public float roll_velocity;
    }

    [Table(Accessor = "run_state")]
    public partial struct RunState
    {
        [PrimaryKey]
        public int id;
        public uint shuffle_seed;
    }

    [Table(Accessor = "config", Public = true)]
    public partial struct Config
    {
        [PrimaryKey]
        public int id;
        public float world_radius;
        public float summit_height;
        public float sandwich_speed;
        public float gravity;
        public float recovery_distance;
        public float summit_distance;
        public float topping_drop_tilt;
        public float topping_drop_impact_speed;
        public float jump_impulse;
    }

    [Table(Accessor = "player", Public = true)]
    [Table(Accessor = "logged_out_player")]
    public partial struct Player
    {
        [PrimaryKey]
        public Identity identity;

        [Unique, AutoInc]
        public int player_id;

        public string name;
        public DbVector3 position;
        public DbVector3 attachment_offset;
        public DbVector3 input_direction;
        public bool jump_queued;
    }

    [Table(Accessor = "sandwich", Public = true)]
    public partial struct Sandwich
    {
        [PrimaryKey]
        public int id;
        public DbVector3 position;
        public DbVector3 velocity;
        public float tilt;
        public float pitch;
        public float roll;
        public int attached_player_count;
        public bool at_summit;
        public bool completed;
        public ulong tick;
    }

    [Table(Accessor = "topping", Public = true)]
    public partial struct Topping
    {
        [PrimaryKey, AutoInc]
        public int topping_id;
        public string name;
        public int layer_order;
        public ToppingState state;
        public DbVector3 position;
        public DbVector3 velocity;
        public DbVector3 attached_offset;
        public int drop_count;
    }

    [Table(Accessor = "game_event", Public = true, Event = true)]
    public partial struct GameEvent
    {
        public Timestamp created_at;
        public string event_type;
        public int player_id;
        public int topping_id;
        public string message;
    }

    [Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        ctx.Db.config.Insert(new Config
        {
            id = 0,
            world_radius = TerrainHeightData.WorldRadius,
            summit_height = TerrainHeightData.SummitHeight,
            sandwich_speed = DefaultSandwichSpeed,
            gravity = DefaultGravity,
            recovery_distance = 3f,
            summit_distance = 0f,
            topping_drop_tilt = 32f,
            topping_drop_impact_speed = 7f,
            jump_impulse = DefaultJumpImpulse,
        });

        ctx.Db.sandwich.Insert(CreateInitialSandwich(RequireConfig(ctx)));
        ctx.Db.sandwich_motion.Insert(new SandwichMotion
        {
            sandwich_id = SandwichId,
            pitch_velocity = 0f,
            roll_velocity = 0f,
        });
        ctx.Db.run_state.Insert(new RunState
        {
            id = 0,
            shuffle_seed = InitialShuffleSeed,
        });
        SeedToppings(ctx, AdvanceShuffleSeed(ctx));

        ctx.Db.simulation_timer.Insert(new SimulationTimer
        {
            scheduled_at = new ScheduleAt.Interval(TimeSpan.FromMilliseconds(50)),
        });
    }

    [Reducer(ReducerKind.ClientConnected)]
    public static void Connect(ReducerContext ctx)
    {
        var loggedOut = ctx.Db.logged_out_player.identity.Find(ctx.Sender);
        if (loggedOut is not null)
        {
            ctx.Db.player.Insert(loggedOut.Value with
            {
                input_direction = DbVector3.Zero,
                jump_queued = false,
                position = ResolvePlayerPosition(
                    RequireSandwich(ctx),
                    loggedOut.Value.attachment_offset,
                    RequireConfig(ctx)
                ),
            });
            ctx.Db.player_motion.Insert(new PlayerMotion
            {
                identity = ctx.Sender,
                vertical_velocity = 0f,
            });
            ctx.Db.logged_out_player.identity.Delete(ctx.Sender);
            return;
        }

        var config = RequireConfig(ctx);
        var player = ctx.Db.player.Insert(new Player
        {
            identity = ctx.Sender,
            name = "",
            position = ResolvePlayerPosition(RequireSandwich(ctx), DbVector3.Zero, config),
            attachment_offset = DbVector3.Zero,
            input_direction = DbVector3.Zero,
            jump_queued = false,
        });
        var attachmentOffset = AttachmentOffset(player.player_id);
        ctx.Db.player.identity.Update(player with
        {
            position = ResolvePlayerPosition(RequireSandwich(ctx), attachmentOffset, config),
            attachment_offset = attachmentOffset,
        });
        ctx.Db.player_motion.Insert(new PlayerMotion
        {
            identity = ctx.Sender,
            vertical_velocity = 0f,
        });
    }

    [Reducer(ReducerKind.ClientDisconnected)]
    public static void Disconnect(ReducerContext ctx)
    {
        var player = RequirePlayer(ctx);
        ctx.Db.logged_out_player.Insert(player with
        {
            input_direction = DbVector3.Zero,
            jump_queued = false,
        });
        ctx.Db.player.identity.Delete(ctx.Sender);
        ctx.Db.player_motion.identity.Delete(ctx.Sender);
    }

    [Reducer]
    public static void EnterGame(ReducerContext ctx, string name)
    {
        var player = RequirePlayer(ctx);
        var cleanName = name.Trim();
        if (cleanName.Length is < 1 or > 24)
        {
            throw new Exception("Player name must contain between 1 and 24 characters.");
        }

        ctx.Db.player.identity.Update(player with { name = cleanName });
    }

    [Reducer]
    public static void UpdatePlayerInput(ReducerContext ctx, DbVector3 direction, bool jumpRequested)
    {
        var player = RequirePlayer(ctx);
        if (!IsFinite(direction))
        {
            throw new Exception("Movement input must contain finite values.");
        }

        ctx.Db.player.identity.Update(player with
        {
            input_direction = ClampMagnitude(direction, 1f),
            jump_queued = player.jump_queued || jumpRequested,
        });
    }

    [Reducer]
    public static void TryRecoverTopping(ReducerContext ctx, int toppingId)
    {
        var player = RequirePlayer(ctx);
        var topping = ctx.Db.topping.topping_id.Find(toppingId)
            ?? throw new Exception("Topping not found.");
        var sandwich = RequireSandwich(ctx);
        var config = RequireConfig(ctx);

        if (topping.state != ToppingState.Dropped)
        {
            throw new Exception("Only dropped toppings can be recovered.");
        }

        if (DbVector3.Distance(player.position, topping.position) > config.recovery_distance)
        {
            throw new Exception("Player is too far from the topping.");
        }

        if (DbVector3.Distance(sandwich.position, topping.position) > config.recovery_distance * 2f)
        {
            throw new Exception("Bring the sandwich closer before recovering this topping.");
        }

        ctx.Db.topping.topping_id.Update(topping with
        {
            state = ToppingState.Attached,
            position = sandwich.position + RotateOffset(topping.attached_offset, sandwich),
            velocity = DbVector3.Zero,
        });
        Emit(ctx, "topping_recovered", player.player_id, topping.topping_id, $"{player.name} recovered {topping.name}.");
    }

    [Reducer]
    public static void ResetRun(ReducerContext ctx)
    {
        var player = RequirePlayer(ctx);
        if (string.IsNullOrWhiteSpace(player.name))
        {
            throw new Exception("Enter the game before resetting the run.");
        }

        foreach (var topping in ctx.Db.topping.Iter().ToList())
        {
            ctx.Db.topping.topping_id.Delete(topping.topping_id);
        }

        var config = RequireConfig(ctx);
        ctx.Db.sandwich.id.Update(CreateInitialSandwich(config));
        ctx.Db.sandwich_motion.sandwich_id.Update(new SandwichMotion
        {
            sandwich_id = SandwichId,
            pitch_velocity = 0f,
            roll_velocity = 0f,
        });
        SeedToppings(ctx, AdvanceShuffleSeed(ctx));

        foreach (var activePlayer in ctx.Db.player.Iter().ToList())
        {
            ctx.Db.player.identity.Update(activePlayer with
            {
                position = ResolvePlayerPosition(RequireSandwich(ctx), activePlayer.attachment_offset, config),
                input_direction = DbVector3.Zero,
                jump_queued = false,
            });
            ctx.Db.player_motion.identity.Update(new PlayerMotion
            {
                identity = activePlayer.identity,
                vertical_velocity = 0f,
            });
        }

        Emit(ctx, "run_reset", player.player_id, 0, $"{player.name} reset the delivery.");
    }

    [Reducer]
    public static void Simulate(ReducerContext ctx, SimulationTimer _timer)
    {
        var config = RequireConfig(ctx);
        var sandwich = RequireSandwich(ctx);
        var sandwichMotion = RequireSandwichMotion(ctx);
        var players = ctx.Db.player.Iter().ToList();
        var impacted = false;
        var disagreementTilt = 0f;

        if (!sandwich.completed)
        {
            var previousSandwichY = sandwich.position.y;
            var jumpPitch = 0f;
            var jumpRoll = 0f;
            if (players.Count > 0)
            {
                var averageInput = DbVector3.Zero;
                foreach (var player in players)
                {
                    averageInput += player.input_direction;
                }
                averageInput /= players.Count;

                var disagreement = 0f;
                foreach (var player in players)
                {
                    disagreement += DbVector3.Distance(player.input_direction, averageInput);
                }
                disagreement /= players.Count;

                var horizontalInput = new DbVector3(averageInput.x, 0f, averageInput.z);
                var targetVelocity = ClampMagnitude(horizontalInput, 1f) * config.sandwich_speed;
                sandwich.velocity = DbVector3.Lerp(
                    new DbVector3(sandwich.velocity.x, 0f, sandwich.velocity.z),
                    targetVelocity,
                    0.35f
                );
                disagreementTilt = disagreement * 45f;
            }
            else
            {
                sandwich.velocity = new DbVector3(
                    sandwich.velocity.x * 0.9f,
                    sandwich.velocity.y,
                    sandwich.velocity.z * 0.9f
                );
                disagreementTilt = 0f;
            }

            sandwich.position += new DbVector3(sandwich.velocity.x, 0f, sandwich.velocity.z) * TickSeconds;
            sandwich.position = ClampToTerrainBounds(sandwich.position, config);
            var playerStates = SimulatePlayers(ctx, players, sandwich, config);
            var targetSandwichHeight = TerrainHeight(sandwich.position, config) + SandwichCarryHeight;

            if (playerStates.Count > 0)
            {
                var averageJumpOffset = 0f;
                var averageVerticalVelocity = 0f;

                foreach (var state in playerStates)
                {
                    averageJumpOffset += state.JumpOffset;
                    averageVerticalVelocity += state.VerticalVelocity;
                    jumpPitch += state.Player.attachment_offset.z * state.JumpOffset;
                    jumpRoll += state.Player.attachment_offset.x * state.JumpOffset;
                }

                averageJumpOffset /= playerStates.Count;
                averageVerticalVelocity /= playerStates.Count;
                jumpPitch /= playerStates.Count * MathF.Max(PlayerCarryRadius, 0.001f);
                jumpRoll /= playerStates.Count * MathF.Max(PlayerCarryRadius, 0.001f);

                sandwich.position.y = targetSandwichHeight + averageJumpOffset * JumpLiftInfluence;
                sandwich.velocity.y = averageVerticalVelocity;
            }
            else
            {
                impacted = sandwich.velocity.y < -config.topping_drop_impact_speed;
                sandwich.position.y = targetSandwichHeight;
                sandwich.velocity.y = 0f;
            }

            var loadCenter = AttachedToppingLoadCenter(ctx);
            var movementPitchTorque =
                -sandwich.velocity.z / MathF.Max(config.sandwich_speed, 0.001f) * MovementPitchTorqueFactor;
            var movementRollTorque =
                sandwich.velocity.x / MathF.Max(config.sandwich_speed, 0.001f) * MovementRollTorqueFactor;
            var jumpPitchTorque = -jumpPitch * JumpPitchTorqueFactor;
            var jumpRollTorque = jumpRoll * JumpRollTorqueFactor;
            var loadPitchTorque = -loadCenter.z * LoadPitchTorqueFactor;
            var loadRollTorque = loadCenter.x * LoadRollTorqueFactor;

            sandwichMotion.pitch_velocity += (
                movementPitchTorque +
                jumpPitchTorque +
                loadPitchTorque -
                sandwich.pitch * AngularReturnSpring
            ) * TickSeconds;
            sandwichMotion.roll_velocity += (
                movementRollTorque +
                jumpRollTorque +
                loadRollTorque -
                sandwich.roll * AngularReturnSpring
            ) * TickSeconds;

            sandwichMotion.pitch_velocity *= AngularVelocityDamping;
            sandwichMotion.roll_velocity *= AngularVelocityDamping;

            if (impacted)
            {
                sandwichMotion.pitch_velocity += -sandwich.pitch * AngularImpactKick * TickSeconds;
                sandwichMotion.roll_velocity += -sandwich.roll * AngularImpactKick * TickSeconds;
            }

            sandwich.pitch = Math.Clamp(
                sandwich.pitch + sandwichMotion.pitch_velocity * TickSeconds,
                -MaxSandwichAngle,
                MaxSandwichAngle
            );
            sandwich.roll = Math.Clamp(
                sandwich.roll + sandwichMotion.roll_velocity * TickSeconds,
                -MaxSandwichAngle,
                MaxSandwichAngle
            );

            impacted |=
                previousSandwichY > targetSandwichHeight + GroundedHeightTolerance &&
                sandwich.position.y <= targetSandwichHeight + GroundedHeightTolerance &&
                sandwich.velocity.y < -config.topping_drop_impact_speed;

            sandwich.tilt = MathF.Sqrt(sandwich.pitch * sandwich.pitch + sandwich.roll * sandwich.roll) + disagreementTilt;
            sandwich.attached_player_count = players.Count;
            sandwich.tick++;

            if (impacted)
            {
                ApplyImpactToAttachedToppings(ctx);
            }

            sandwich.at_summit = false;
            sandwich.completed = false;
        }

        ctx.Db.sandwich.id.Update(sandwich);
        ctx.Db.sandwich_motion.sandwich_id.Update(sandwichMotion);
        UpdateAttachedToppings(ctx, sandwich, sandwichMotion, config);
        UpdateDroppedToppings(ctx, config);
    }

    private static Config RequireConfig(ReducerContext ctx)
        => ctx.Db.config.id.Find(0) ?? throw new Exception("Config not found.");

    private static Sandwich RequireSandwich(ReducerContext ctx)
        => ctx.Db.sandwich.id.Find(SandwichId) ?? throw new Exception("Sandwich not found.");

    private static Player RequirePlayer(ReducerContext ctx)
        => ctx.Db.player.identity.Find(ctx.Sender) ?? throw new Exception("Player not found.");

    private static PlayerMotion RequirePlayerMotion(ReducerContext ctx, Identity identity)
        => ctx.Db.player_motion.identity.Find(identity) ?? throw new Exception("Player motion not found.");

    private static SandwichMotion RequireSandwichMotion(ReducerContext ctx)
        => ctx.Db.sandwich_motion.sandwich_id.Find(SandwichId) ?? throw new Exception("Sandwich motion not found.");

    private static RunState RequireRunState(ReducerContext ctx)
        => ctx.Db.run_state.id.Find(0) ?? throw new Exception("Run state not found.");

    private static Sandwich CreateInitialSandwich(Config config)
    {
        var horizontalPosition = new DbVector3(TerrainHeightData.CenterX, 0f, TerrainHeightData.CenterZ);
        return new Sandwich
        {
            id = SandwichId,
            position = horizontalPosition + new DbVector3(
                0f,
                TerrainHeight(horizontalPosition, config) + SandwichCarryHeight,
                0f
            ),
            velocity = DbVector3.Zero,
        };
    }

    private static void SeedToppings(ReducerContext ctx, uint shuffleSeed)
    {
        var bottomBread = ToppingShapeData.GetProfile("Bottom Bread");
        InsertTopping(ctx, bottomBread.Name, 0, new DbVector3(0f, 0f, 0f), ToppingState.Attached);

        var fillings = new[]
        {
            ToppingShapeData.GetProfile("Lettuce"),
            ToppingShapeData.GetProfile("Tomato"),
            ToppingShapeData.GetProfile("Cheese"),
        };
        ShuffleFillings(fillings, shuffleSeed);

        var currentTop = bottomBread.Thickness * 0.5f;
        for (var i = 0; i < fillings.Length; i++)
        {
            var profile = fillings[i];
            var centerY = currentTop + profile.Thickness * 0.5f;
            InsertTopping(
                ctx,
                profile.Name,
                i + 1,
                new DbVector3(0f, centerY, 0f),
                ToppingState.Attached
            );
            currentTop += profile.Thickness;
        }

        var topBread = ToppingShapeData.GetProfile("Top Bread");
        var topBreadY = currentTop + topBread.Thickness * 0.5f;
        InsertTopping(ctx, topBread.Name, 4, new DbVector3(0f, topBreadY, 0f), ToppingState.Attached);
    }

    private static void InsertTopping(
        ReducerContext ctx,
        string name,
        int layerOrder,
        DbVector3 offset,
        ToppingState state,
        DbVector3? position = null
    )
    {
        var sandwich = RequireSandwich(ctx);
        ctx.Db.topping.Insert(new Topping
        {
            name = name,
            layer_order = layerOrder,
            state = state,
            position = position ?? sandwich.position + offset,
            velocity = DbVector3.Zero,
            attached_offset = offset,
        });
    }

    private static void UpdateAttachedToppings(
        ReducerContext ctx,
        Sandwich sandwich,
        SandwichMotion sandwichMotion,
        Config config
    )
    {
        foreach (var topping in ctx.Db.topping.Iter().Where(row =>
            row.state is ToppingState.Attached or ToppingState.Placed
        ).ToList())
        {
            if (topping.layer_order == 0)
            {
                ctx.Db.topping.topping_id.Update(topping with
                {
                    position = sandwich.position + RotateOffset(topping.attached_offset, sandwich),
                    velocity = DbVector3.Zero,
                });
                continue;
            }

            var profile = ToppingShapeData.GetProfile(topping.name);
            var localVelocity = topping.velocity;
            var localOffset = topping.attached_offset;
            var slideAcceleration = LocalSlideAcceleration(sandwich, sandwichMotion, localOffset, config, profile);
            localVelocity.x = (localVelocity.x + slideAcceleration.x * TickSeconds) * profile.Friction;
            localVelocity.z = (localVelocity.z + slideAcceleration.z * TickSeconds) * profile.Friction;
            localVelocity.y = 0f;

            localOffset.x += localVelocity.x * TickSeconds;
            localOffset.z += localVelocity.z * TickSeconds;

            var boundary = MathF.Max(ToppingSlideBoundary, profile.SlideBoundary);
            if (MathF.Abs(localOffset.x) > boundary || MathF.Abs(localOffset.z) > boundary)
            {
                DropAttachedTopping(ctx, sandwich, topping, localOffset, localVelocity);
                continue;
            }

            ctx.Db.topping.topping_id.Update(topping with
            {
                attached_offset = localOffset,
                position = sandwich.position + RotateOffset(localOffset, sandwich),
                velocity = localVelocity,
            });
        }
    }

    private static void UpdateDroppedToppings(ReducerContext ctx, Config config)
    {
        foreach (var topping in ctx.Db.topping.Iter().Where(row => row.state == ToppingState.Dropped).ToList())
        {
            var velocity = topping.velocity;
            velocity.y -= config.gravity * TickSeconds;

            var position = topping.position + velocity * TickSeconds;
            var terrainHeight = TerrainHeight(position, config);
            if (position.y <= terrainHeight)
            {
                position.y = terrainHeight;
                velocity = DbVector3.Zero;
            }
            position = ClampToTerrainBounds(position, config);

            ctx.Db.topping.topping_id.Update(topping with
            {
                position = position,
                velocity = velocity,
            });
        }
    }

    private static void ApplyImpactToAttachedToppings(ReducerContext ctx)
    {
        foreach (var topping in ctx.Db.topping.Iter()
            .Where(row => row.state == ToppingState.Attached && row.layer_order > 0)
            .ToList())
        {
            var profile = ToppingShapeData.GetProfile(topping.name);
            var localVelocity = topping.velocity;
            var directionX = topping.attached_offset.x;
            var directionZ = topping.attached_offset.z;
            var directionLength = MathF.Sqrt(directionX * directionX + directionZ * directionZ);
            if (directionLength < 0.001f)
            {
                directionX = (topping.layer_order % 2 == 0) ? 1f : -1f;
                directionZ = topping.layer_order >= 3 ? 1f : -1f;
                directionLength = MathF.Sqrt(directionX * directionX + directionZ * directionZ);
            }

            localVelocity.x += directionX / directionLength * (ImpactSlideImpulse / profile.Mass);
            localVelocity.z += directionZ / directionLength * (ImpactSlideImpulse / profile.Mass);

            ctx.Db.topping.topping_id.Update(topping with
            {
                velocity = localVelocity,
            });
        }
    }

    private static System.Collections.Generic.List<SimulatedPlayerState> SimulatePlayers(
        ReducerContext ctx,
        System.Collections.Generic.List<Player> players,
        Sandwich sandwich,
        Config config
    )
    {
        var states = new System.Collections.Generic.List<SimulatedPlayerState>(players.Count);
        foreach (var player in players)
        {
            var motion = RequirePlayerMotion(ctx, player.identity);
            var nextHorizontalPosition = new DbVector3(
                sandwich.position.x + player.attachment_offset.x,
                0f,
                sandwich.position.z + player.attachment_offset.z
            );
            nextHorizontalPosition = ClampToTerrainBounds(nextHorizontalPosition, config);

            var currentGroundHeight = TerrainHeight(player.position, config);
            var jumpOffset = MathF.Max(0f, player.position.y - currentGroundHeight);
            var verticalVelocity = motion.vertical_velocity;

            if (player.jump_queued && jumpOffset <= GroundedHeightTolerance)
            {
                verticalVelocity = config.jump_impulse;
            }

            verticalVelocity -= config.gravity * TickSeconds;
            jumpOffset += verticalVelocity * TickSeconds;
            if (jumpOffset <= 0f)
            {
                jumpOffset = 0f;
                verticalVelocity = 0f;
            }

            var nextGroundHeight = TerrainHeight(nextHorizontalPosition, config);
            var nextPosition = new DbVector3(
                nextHorizontalPosition.x,
                nextGroundHeight + jumpOffset,
                nextHorizontalPosition.z
            );

            var updatedPlayer = player with
            {
                position = nextPosition,
                jump_queued = false,
            };
            var updatedMotion = motion with
            {
                vertical_velocity = verticalVelocity,
            };

            ctx.Db.player.identity.Update(updatedPlayer);
            ctx.Db.player_motion.identity.Update(updatedMotion);
            states.Add(new SimulatedPlayerState(updatedPlayer, jumpOffset, verticalVelocity));
        }

        return states;
    }

    private static DbVector3 AttachmentOffset(int index)
    {
        var angle = index * 2.3999632f;
        return new DbVector3(
            MathF.Cos(angle) * PlayerCarryRadius,
            0f,
            MathF.Sin(angle) * PlayerCarryRadius
        );
    }

    private static DbVector3 ClampMagnitude(DbVector3 value, float maximum)
    {
        var magnitude = value.Magnitude;
        return magnitude > maximum ? value / magnitude * maximum : value;
    }

    private static bool IsFinite(DbVector3 value)
        => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    private static float Lerp(float from, float to, float weight) => from + (to - from) * weight;

    private static DbVector3 ClampToTerrainBounds(DbVector3 position, Config config)
    {
        position.x = Math.Clamp(position.x, TerrainHeightData.MinX, TerrainHeightData.MaxX);
        position.z = Math.Clamp(position.z, TerrainHeightData.MinZ, TerrainHeightData.MaxZ);

        position.y = Math.Clamp(position.y, 0f, config.summit_height + SandwichCarryHeight + MaxAirHeightBuffer);
        return position;
    }

    private static float TerrainHeight(DbVector3 position, Config config)
        => TerrainHeightData.SampleHeight(position.x, position.z);

    private static DbVector3 LocalSlideAcceleration(
        Sandwich sandwich,
        SandwichMotion sandwichMotion,
        DbVector3 localOffset,
        Config config,
        ToppingShapeProfile profile
    )
    {
        var pitchRadians = DegreesToRadians(sandwich.pitch);
        var rollRadians = DegreesToRadians(sandwich.roll);
        var angularPitchAccel = DegreesToRadians(sandwichMotion.pitch_velocity) * (localOffset.y + 0.2f);
        var angularRollAccel = DegreesToRadians(sandwichMotion.roll_velocity) * (localOffset.y + 0.2f);
        return new DbVector3(
            (
                MathF.Sin(rollRadians) * config.gravity * ToppingSlideAccelerationFactor +
                angularRollAccel * ToppingAngularAccelerationFactor
            ) / profile.Mass,
            0f,
            (
                -MathF.Sin(pitchRadians) * config.gravity * ToppingSlideAccelerationFactor -
                angularPitchAccel * ToppingAngularAccelerationFactor
            ) / profile.Mass
        );
    }

    private static DbVector3 AttachedToppingLoadCenter(ReducerContext ctx)
    {
        var totalMass = 0f;
        var weightedX = 0f;
        var weightedZ = 0f;

        foreach (var topping in ctx.Db.topping.Iter().Where(row => row.state == ToppingState.Attached && row.layer_order > 0))
        {
            var profile = ToppingShapeData.GetProfile(topping.name);
            totalMass += profile.Mass;
            weightedX += topping.attached_offset.x * profile.Mass;
            weightedZ += topping.attached_offset.z * profile.Mass;
        }

        if (totalMass <= 0.0001f)
        {
            return DbVector3.Zero;
        }

        return new DbVector3(weightedX / totalMass, 0f, weightedZ / totalMass);
    }

    private static uint AdvanceShuffleSeed(ReducerContext ctx)
    {
        var runState = RequireRunState(ctx);
        var nextSeed = unchecked(runState.shuffle_seed * 1664525u + 1013904223u);
        ctx.Db.run_state.id.Update(runState with { shuffle_seed = nextSeed });
        return nextSeed;
    }

    private static void ShuffleFillings(ToppingShapeProfile[] profiles, uint seed)
    {
        for (var i = profiles.Length - 1; i > 0; i--)
        {
            seed = unchecked(seed * 1664525u + 1013904223u);
            var swapIndex = (int)(seed % (uint)(i + 1));
            (profiles[i], profiles[swapIndex]) = (profiles[swapIndex], profiles[i]);
        }
    }

    private static DbVector3 RotateOffset(DbVector3 offset, Sandwich sandwich)
    {
        var pitchRadians = DegreesToRadians(sandwich.pitch);
        var rollRadians = DegreesToRadians(sandwich.roll);

        var cosPitch = MathF.Cos(pitchRadians);
        var sinPitch = MathF.Sin(pitchRadians);
        var cosRoll = MathF.Cos(rollRadians);
        var sinRoll = MathF.Sin(rollRadians);

        var pitchedY = offset.y * cosPitch - offset.z * sinPitch;
        var pitchedZ = offset.y * sinPitch + offset.z * cosPitch;

        var rolledX = offset.x * cosRoll - pitchedY * sinRoll;
        var rolledY = offset.x * sinRoll + pitchedY * cosRoll;

        return new DbVector3(rolledX, rolledY, pitchedZ);
    }

    private static void DropAttachedTopping(
        ReducerContext ctx,
        Sandwich sandwich,
        Topping topping,
        DbVector3 localOffset,
        DbVector3 localVelocity
    )
    {
        var worldOffset = RotateOffset(localOffset, sandwich);
        var worldSlideVelocity = RotateOffset(localVelocity, sandwich);

        ctx.Db.topping.topping_id.Update(topping with
        {
            state = ToppingState.Dropped,
            attached_offset = localOffset,
            position = sandwich.position + worldOffset,
            velocity = sandwich.velocity + worldSlideVelocity,
            drop_count = topping.drop_count + 1,
        });
        Emit(ctx, "topping_dropped", 0, topping.topping_id, $"{topping.name} slid off the sandwich.");
    }

    private static DbVector3 ResolvePlayerPosition(
        Sandwich sandwich,
        DbVector3 attachmentOffset,
        Config config
    )
    {
        var horizontalPosition = new DbVector3(
            sandwich.position.x + attachmentOffset.x,
            0f,
            sandwich.position.z + attachmentOffset.z
        );
        horizontalPosition = ClampToTerrainBounds(horizontalPosition, config);
        var sandwichGroundHeight = TerrainHeight(sandwich.position, config) + SandwichCarryHeight;
        var airborneOffset = MathF.Max(0f, sandwich.position.y - sandwichGroundHeight);
        horizontalPosition.y = TerrainHeight(horizontalPosition, config) + airborneOffset;
        return horizontalPosition;
    }

    private readonly record struct SimulatedPlayerState(Player Player, float JumpOffset, float VerticalVelocity);

    private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180f);

    private static void Emit(
        ReducerContext ctx,
        string eventType,
        int playerId,
        int toppingId,
        string message
    )
    {
        ctx.Db.game_event.Insert(new GameEvent
        {
            created_at = ctx.Timestamp,
            event_type = eventType,
            player_id = playerId,
            topping_id = toppingId,
            message = message,
        });
    }
}
