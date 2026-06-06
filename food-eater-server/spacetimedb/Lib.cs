using System;
using SpacetimeDB;

public static partial class Module
{
    // Scheduled reducer timer table.
    [Table(Accessor = "spawn_food_timer", Scheduled = nameof(SpawnFood), ScheduledAt = nameof(scheduled_at))]
    public partial struct SpawnFoodTimer
    {
        [PrimaryKey, AutoInc]
        public ulong scheduled_id;

        public ScheduleAt scheduled_at;
    }

    // We're using this table as a singleton, so in this table
    // there will only be one element where the `id` is 0.
    [Table(Accessor = "config", Public = true)]
    public partial struct Config
    {
        [PrimaryKey]
        public int id;

        public long world_size;
    }

    [Table(Accessor = "entity", Public = true)]
    public partial struct Entity
    {
        [PrimaryKey, AutoInc]
        public int entity_id;

        public DbVector2 position;

        public int mass;
    }

    [Table(Accessor = "circle", Public = true)]
    public partial struct Circle
    {
        [PrimaryKey]
        public int entity_id;

        [SpacetimeDB.Index.BTree]
        public int player_id;

        public DbVector2 direction;

        public float speed;

        public Timestamp last_split_time;
    }

    [Table(Accessor = "food", Public = true)]
    public partial struct Food
    {
        [PrimaryKey]
        public int entity_id;
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
    }

    [Reducer(ReducerKind.Init)]
public static void Init(ReducerContext ctx)
{
    Log.Info($"Initializing...");

    ctx.Db.config.Insert(new Config
    {
        world_size = 1000
    });

    ctx.Db.spawn_food_timer.Insert(new SpawnFoodTimer
    {
        scheduled_at = new ScheduleAt.Interval(TimeSpan.FromMilliseconds(500))
    });

    ctx.Db.move_all_players_timer.Insert(new MoveAllPlayersTimer
    {
        scheduled_at = new ScheduleAt.Interval(TimeSpan.FromMilliseconds(50))
    });
}
    [Reducer(ReducerKind.ClientConnected)]
    public static void Connect(ReducerContext ctx)
    {
        var player = ctx.Db.logged_out_player.identity.Find(ctx.Sender);

        if (player != null)
        {
            ctx.Db.player.Insert(player.Value);
            ctx.Db.logged_out_player.identity.Delete(player.Value.identity);
        }
        else
        {
            ctx.Db.player.Insert(new Player
            {
                identity = ctx.Sender,
                name = "",
            });
        }
    }

    [Reducer(ReducerKind.ClientDisconnected)]
    public static void Disconnect(ReducerContext ctx)
    {
        var player = ctx.Db.player.identity.Find(ctx.Sender)
            ?? throw new Exception("Player not found");

        // Remove any circles from the arena.
        foreach (var circle in ctx.Db.circle.player_id.Filter(player.player_id))
        {
            var entity = ctx.Db.entity.entity_id.Find(circle.entity_id)
                ?? throw new Exception("Could not find circle");

            ctx.Db.entity.entity_id.Delete(entity.entity_id);
            ctx.Db.circle.entity_id.Delete(entity.entity_id);
        }

        ctx.Db.logged_out_player.Insert(player);
        ctx.Db.player.identity.Delete(player.identity);
    }

    const int FOOD_MASS_MIN = 2;
    const int FOOD_MASS_MAX = 4;
    const int TARGET_FOOD_COUNT = 600;

    public static float MassToRadius(int mass) => MathF.Sqrt(mass);

    [Reducer]
    public static void SpawnFood(ReducerContext ctx, SpawnFoodTimer _timer)
    {
        // Are there no players yet?
        if (ctx.Db.player.Count == 0)
        {
            return;
        }

        var worldSize = (ctx.Db.config.id.Find(0)
            ?? throw new Exception("Config not found")).world_size;

        var rng = ctx.Rng;
        var foodCount = ctx.Db.food.Count;

        while (foodCount < TARGET_FOOD_COUNT)
        {
            var foodMass = rng.Range(FOOD_MASS_MIN, FOOD_MASS_MAX);
            var foodRadius = MassToRadius(foodMass);

            var x = rng.Range(foodRadius, worldSize - foodRadius);
            var y = rng.Range(foodRadius, worldSize - foodRadius);

            var entity = ctx.Db.entity.Insert(new Entity
            {
                position = new DbVector2(x, y),
                mass = foodMass,
            });

            ctx.Db.food.Insert(new Food
            {
                entity_id = entity.entity_id,
            });

            foodCount++;
            Log.Info($"Spawned food! {entity.entity_id}");
        }
    }

    public static float Range(this Random rng, float min, float max)
        => rng.NextSingle() * (max - min) + min;

    public static int Range(this Random rng, int min, int max)
        => (int)rng.NextInt64(min, max);

    const int START_PLAYER_MASS = 15;

    [Reducer]
    public static void EnterGame(ReducerContext ctx, string name)
    {
        Log.Info($"Creating player with name {name}");

        var player = ctx.Db.player.identity.Find(ctx.Sender)
            ?? throw new Exception("Player not found");

        player.name = name;
        ctx.Db.player.identity.Update(player);

        SpawnPlayerInitialCircle(ctx, player.player_id);
    }

    public static Entity SpawnPlayerInitialCircle(ReducerContext ctx, int playerId)
    {
        var rng = ctx.Rng;

        var worldSize = (ctx.Db.config.id.Find(0)
            ?? throw new Exception("Config not found")).world_size;

        var playerStartRadius = MassToRadius(START_PLAYER_MASS);

        var x = rng.Range(playerStartRadius, worldSize - playerStartRadius);
        var y = rng.Range(playerStartRadius, worldSize - playerStartRadius);

        return SpawnCircleAt(
            ctx,
            playerId,
            START_PLAYER_MASS,
            new DbVector2(x, y),
            ctx.Timestamp
        );
    }

    public static Entity SpawnCircleAt(
        ReducerContext ctx,
        int playerId,
        int mass,
        DbVector2 position,
        Timestamp timestamp
    )
    {
        var entity = ctx.Db.entity.Insert(new Entity
        {
            position = position,
            mass = mass,
        });

        ctx.Db.circle.Insert(new Circle
        {
            entity_id = entity.entity_id,
            player_id = playerId,
            direction = new DbVector2(0, 1),
            speed = 0f,
            last_split_time = timestamp,
        });

        return entity;
    }



    [Reducer]
public static void UpdatePlayerInput(ReducerContext ctx, DbVector2 direction)
{
    var player = ctx.Db.player.identity.Find(ctx.Sender) ?? throw new Exception("Player not found");
    foreach (var c in ctx.Db.circle.player_id.Filter(player.player_id))
    {
        var circle = c;
        circle.direction = direction.Normalized;
        circle.speed = Math.Clamp(direction.Magnitude, 0f, 1f);
        ctx.Db.circle.entity_id.Update(circle);
    }
}

[Table(Accessor = "move_all_players_timer", Scheduled = nameof(MoveAllPlayers), ScheduledAt = nameof(scheduled_at))]
public partial struct MoveAllPlayersTimer
{
    [PrimaryKey, AutoInc]
    public ulong scheduled_id;
    public ScheduleAt scheduled_at;
}

const int START_PLAYER_SPEED = 10;

public static float MassToMaxMoveSpeed(int mass) => 2f * START_PLAYER_SPEED / (1f + MathF.Sqrt((float)mass / START_PLAYER_MASS));

private const float MINIMUM_SAFE_MASS_RATIO = 0.85f;

public static bool IsOverlapping(Entity a, Entity b)
{
    var dx = a.position.x - b.position.x;
    var dy = a.position.y - b.position.y;
    var distanceSq = dx * dx + dy * dy;

    var aRadius = MassToRadius(a.mass);
    var bRadius = MassToRadius(b.mass);

    // If the distance between the two circle centers is less than the
    // maximum radius, then the center of the smaller circle is inside
    // the larger circle. This gives some leeway for the circles to overlap
    // before being eaten.
    var maxRadius = aRadius > bRadius ? aRadius: bRadius;
    return distanceSq <= maxRadius * maxRadius;
}

[Reducer]
public static void MoveAllPlayers(ReducerContext ctx, MoveAllPlayersTimer timer)
{
    var worldSize = (ctx.Db.config.id.Find(0) ?? throw new Exception("Config not found")).world_size;

    // Handle player input
    foreach (var circle in ctx.Db.circle.Iter())
    {
        var checkEntity = ctx.Db.entity.entity_id.Find(circle.entity_id);
        if (checkEntity == null)
        {
            // This can happen if the circle has been eaten by another circle.
            continue;
        }
        var circleEntity = checkEntity.Value;
        var circleRadius = MassToRadius(circleEntity.mass);
        var direction = circle.direction * circle.speed;
        var newPosition = circleEntity.position + direction * MassToMaxMoveSpeed(circleEntity.mass);
        circleEntity.position.x = Math.Clamp(newPosition.x, circleRadius, worldSize - circleRadius);
        circleEntity.position.y = Math.Clamp(newPosition.y, circleRadius, worldSize - circleRadius);

        // Check collisions
        foreach (var entity in ctx.Db.entity.Iter())
        {
            if (entity.entity_id == circleEntity.entity_id || !IsOverlapping(circleEntity, entity))  continue;

            // Check to see if we're overlapping with food
            if (ctx.Db.food.entity_id.Find(entity.entity_id).HasValue) {
                ctx.Db.entity.entity_id.Delete(entity.entity_id);
                ctx.Db.food.entity_id.Delete(entity.entity_id);
                circleEntity.mass += entity.mass;

                continue;
            }

            // Check to see if we're overlapping with another circle owned by another player
            var otherCircle = ctx.Db.circle.entity_id.Find(entity.entity_id);
            if (otherCircle.HasValue && otherCircle.Value.player_id != circle.player_id)
            {
                var massRatio = (float)entity.mass / circleEntity.mass;
                if (massRatio < MINIMUM_SAFE_MASS_RATIO)
                {
                    ctx.Db.entity.entity_id.Delete(entity.entity_id);
                    ctx.Db.circle.entity_id.Delete(entity.entity_id);
                    circleEntity.mass += entity.mass;
                }
            }
        }
        ctx.Db.entity.entity_id.Update(circleEntity);
    }
}


}
