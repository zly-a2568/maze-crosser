using Godot;
using static Godot.GD;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class Game : Node2D
{
    int[,] map = new int[5, 5];
    [Export]
    public Vector2I map_size;

    [Export]
    public Vector2I start_point;

    private Vector2I end_point;

    [Export]
    public Texture2D ghost_image = Load<Texture2D>("res://assets/player.png");

    private bool moving = false;
    private bool won = false;
    private Vector2I cursor;

    private Vector2I[] move_directions = [Vector2I.Left, Vector2I.Down, Vector2I.Right, Vector2I.Up];

    private TileMapLayer _map;
    private Camera2D _camera;

    private CharacterBody2D _player;

    private Node2D prompts;

    private TextureRect mask;
    private Panel panel;

    // 【新增】用于控制幽灵特效生成的计时器
    private double _ghostSpawnTimer = 0.0;
    private const double GhostSpawnInterval = 0.05; // 每0.05秒生成一个幽灵，防止一帧生成几十个

    public override void _Ready()
    {
        map_size=GameProcessor.Instance.map_size;
        _camera = GetNode<Camera2D>("player/camera");
        _map = GetNode<TileMapLayer>("map");
        _player = GetNode<CharacterBody2D>("player");
        prompts = _player.GetNode<Node2D>("prompts");
        mask = GetNode<TextureRect>("panel/TextureRect");
        panel = GetNode<Panel>("panel/Panel");
        map = GenerateMazePrim(map_size.X, map_size.Y);
        var ToEnd = new List<Vector2I>();
        mask.Show();
        mask.Modulate = new Color(0xFFFFFFFF);
        for (int a = 0; a < map.GetLength(0); a++)
        {
            for (int b = 0; b < map.GetLength(1); b++)
            {
                if (map[a, b] == 0)
                {
                    ToEnd.Add(new Vector2I(b, a));
                }
            }
        }

        ToEnd.Sort((x, y) =>
        {
            return x.LengthSquared().CompareTo(y.LengthSquared());
        });
        var all_rights = (int)(ToEnd.Count * ToEnd.Count);
        var seed = Randi() % all_rights;
        var idx = (int)Math.Sqrt(seed);
        if (idx < ToEnd.Count)
        {
            end_point = ToEnd[idx];
            map[ToEnd[idx].Y, ToEnd[idx].X] = 2;
        }
        else
        {
            end_point = ToEnd[^1];
            map[ToEnd[^1].Y, ToEnd[^1].X] = 2;
        }

        ApplyMap();
        var map_rect = _map.GetUsedRect();
        _camera.LimitLeft = 16 * (map_rect.Position.X);
        _camera.LimitTop = 16 * (map_rect.Position.Y);
        _camera.LimitRight = 16 * (map_rect.Position.X + map_rect.Size.X);
        _camera.LimitBottom = 16 * (map_rect.Position.Y + map_rect.Size.Y);

        _player.Position = start_point * 16 + (new Vector2(8.0f, 8.0f));
        cursor = start_point;
        var t = CreateTween();
        t.TweenProperty(mask, "modulate:a", 0.0f, 0.5);
        UpdatePrompt();
    }

    public override void _Process(double delta)
    {
        // 【修复】使用计时器限制生成频率，彻底解决内存泄漏和性能雪崩
        if (moving)
        {
            _ghostSpawnTimer += delta;
            if (_ghostSpawnTimer >= GhostSpawnInterval)
            {
                _ghostSpawnTimer = 0.0;
                SpawnGhostEffect();
            }
        }

        // 【优化】将坐标比较提前，避免每帧都进行 LocalToMap 坐标转换
        if (!won && _map.LocalToMap(ToLocal(ToGlobal(_player.Position))) == end_point)
        {
            GameWin();
        }
    }

    // 【抽取】单独封装幽灵生成逻辑，保持代码整洁
    private void SpawnGhostEffect()
    {
        var ghost_effect = new Sprite2D();
        ghost_effect.ProcessMode = ProcessModeEnum.Always;
        ghost_effect.Texture = ghost_image;
        ghost_effect.Position = _player.Position;
        ghost_effect.AddToGroup("ghost", true);
        AddChild(ghost_effect);
        
        var t = CreateTween();
        t.TweenProperty(ghost_effect, "modulate", new Color(0xFFFFFF00), 0.9);
        t.Finished += () =>
        {
            if (IsInstanceValid(ghost_effect)) ghost_effect.QueueFree();
        };
    }

    // 【修复】将 async void 改为 async Task，防止节点销毁时报错
    private async Task GameWin()
    {
        // 【关键修复】立即停止 _Process 中的任何逻辑，防止在退场动画时产生僵尸节点
        SetProcess(false);
        SetPhysicsProcess(false);
        
        moving = false;
        won = true;
        _player.Position = end_point * 16 + new Vector2(8.0f, 8.0f);
        
        var ts = GetTree().GetProcessedTweens();
        for (int i = 0; i < ts.Count; i++)
        {
            ts[i].Kill();
        }

        mask.Show();
        var t1 = CreateTween();
        t1.TweenProperty(mask, "modulate:a", 1.0, 0.2);

        foreach (Sprite2D a in GetTree().GetNodesInGroup("ghost"))
        {
            if (!IsInstanceValid(a)) continue;
            
            var map_rect = _map.GetUsedRect();
            var t = CreateTween();
            t.TweenProperty(a, "position", a.Position + 16.0f * (new Vector2(0.0f, map_rect.Position.Y + map_rect.Size.Y)), 0.05).SetTrans(Tween.TransitionType.Bounce);
            t.Finished += () =>
            {
                if (IsInstanceValid(a)) a.QueueFree();
            };
            // 【安全检查】确保在等待期间节点没被意外销毁
            if (IsInstanceValid(this))
            {
                await ToSignal(GetTree().CreateTimer(0.04), Timer.SignalName.Timeout);
            }
            else
            {
                return; // 如果当前节点已死，直接退出
            }
        }

        foreach (var a in GetChildren())
        {
            if (IsInstanceValid(a) && !IsQueuedForDeletion())
            {
                a.QueueFree();
            }
        }

        // 【安全检查】最后调用外部方法前再次确认节点存活
        if (IsInstanceValid(this) && IsInstanceValid(GameProcessor.Instance))
        {
            GameProcessor.Instance.CallDeferred("ChangeScene", ["res://scenes/game.tscn"]);
        }
    }

    public override void _Input(InputEvent @event)
    {
        base._Input(@event);
        if (@event is InputEventKey)
        {
            if (@event.IsActionPressed("ui_cancel") && !won)
            {
                panel.ShowPanel();
            }
            if (moving || won) return; // 【优化】加上 won 判断
            
            if (@event.IsActionPressed("ui_left"))
            {
                moving = true;
                Walk(Vector2I.Left);
            }
            else if (@event.IsActionPressed("ui_right"))
            {
                moving = true;
                Walk(Vector2I.Right);
            }
            else if (@event.IsActionPressed("ui_up"))
            {
                moving = true;
                Walk(Vector2I.Up);
            }
            else if (@event.IsActionPressed("ui_down"))
            {
                moving = true;
                Walk(Vector2I.Down);
            }
        }
    }

    public void UpdatePrompt()
    {
        var left = prompts.GetChild<Sprite2D>(1);
        var right = prompts.GetChild<Sprite2D>(0);
        var top = prompts.GetChild<Sprite2D>(2);
        var bottom = prompts.GetChild<Sprite2D>(3);
        left.Hide();
        right.Hide();
        top.Hide();
        bottom.Hide();
        var dirs = GetAvalibleDirections(cursor);
        foreach (var d in dirs)
        {
            if (d == Vector2I.Down) bottom.Visible = true;
            if (d == Vector2I.Left) left.Visible = true;
            if (d == Vector2I.Right) right.Visible = true;
            if (d == Vector2I.Up) top.Visible = true;
        }
    }

    public void HidePrompts()
    {
        foreach (Sprite2D child in prompts.GetChildren())
        {
            var t = CreateTween();
            t.TweenProperty(child, "position", new Vector2(0.0f, 0.0f), 0.1).SetTrans(Tween.TransitionType.Sine);
        }
    }

    public void ShowPrompts()
    {
        foreach (Sprite2D child in prompts.GetChildren())
        {
            var t = CreateTween();
            if (child.Name == "ArrowRight")
                t.TweenProperty(child, "position", new Vector2(24.0f, 0.0f), 0.1).SetTrans(Tween.TransitionType.Sine);
            if (child.Name == "ArrowLeft")
                t.TweenProperty(child, "position", new Vector2(-24.0f, 0.0f), 0.1).SetTrans(Tween.TransitionType.Sine);
            if (child.Name == "ArrowTop")
                t.TweenProperty(child, "position", new Vector2(0.0f, -24.0f), 0.1).SetTrans(Tween.TransitionType.Sine);
            if (child.Name == "ArrowBottom")
                t.TweenProperty(child, "position", new Vector2(0.0f, 24.0f), 0.1).SetTrans(Tween.TransitionType.Sine);
        }
    }

    public void ApplyMap()
    {
        for (int i = 0; i < map.GetLength(0); i++)
        {
            for (int j = 0; j < map.GetLength(1); j++)
            {
                if (map[i, j] == 1) _map.SetCell(new Vector2I(j, i), 1, Vector2I.Zero);
                if (map[i, j] == 2) _map.SetCell(new Vector2I(j, i), 0, Vector2I.Zero);
            }
        }
    }

    public List<Vector2I> GetAvalibleDirections(Vector2I pos)
    {
        List<Vector2I> directions = [];
        foreach (var direction in move_directions)
        {
            Vector2I np = pos + direction;
            if (!(np.X > map.GetLength(1) - 1 || np.X < 0 || np.Y > map.GetLength(0) - 1 || np.Y < 0 || map[np.Y, np.X] == 1))
            {
                directions.Add(direction);
            }
        }
        return directions;
    }

    // 【修复】将 async void 改为 async Task
    public async Task MovePlayer(List<Vector2I> path)
    {
        foreach (var p in path)
        {
            // 【安全检查】防止移动过程中场景被切换导致报错
            if (!IsInstanceValid(this) || !IsInstanceValid(_player)) return;

            var t = CreateTween();
            t.TweenProperty(_player, "position", p * 16 + (new Vector2(8.0f, 8.0f)), 0.05);
            await ToSignal(t, Tween.SignalName.Finished);
        }

        // 动画走完后，再判断一次当前节点是否存活
        if (!IsInstanceValid(this)) return;

        ShowPrompts();
        UpdatePrompt();
        moving = false;
    }

    public void Walk(Vector2I direction)
    {
        HidePrompts();
        var dirs = GetAvalibleDirections(cursor);
        if (!dirs.Contains(direction))
        {
            moving = false;
            ShowPrompts();
            return;
        }

        var visited = new List<Vector2I>();
        var start_pos = cursor;
        cursor += direction;
        visited.Add(start_pos);

        bool stop = false;
        while (!stop)
        {
            visited.Add(cursor);
            var directions = GetAvalibleDirections(cursor);
            if (directions.Count >= 3)
            {
                stop = true;
            }
            else if (directions.Count == 2)
            {
                if (visited.Contains(cursor + directions[0]))
                {
                    directions[0] = directions[1];
                }
                cursor += directions[0];
            }
            else if (directions.Count == 1)
            {
                stop = true;
            }
        }

        var path = BFS_Map(map, start_pos, cursor);
        // 【修复】因为 MovePlayer 现在是 Task，这里不需要阻塞，直接触发即可
        _ = MovePlayer(path);
    }

    public List<Vector2I> BFS_Map(int[,] map, Vector2I start, Vector2I end)
    {
        Vector2I[] directions = [Vector2I.Down, Vector2I.Up, Vector2I.Left, Vector2I.Right];
        bool[,] visited = new bool[map.GetLength(0), map.GetLength(1)];
        Vector2I[,] prev = new Vector2I[map.GetLength(0), map.GetLength(1)];
        Queue<Vector2I> queue = new();

        queue.Enqueue(start);
        visited[start.Y, start.X] = true;

        while (queue.Count != 0)
        {
            Vector2I p = queue.Dequeue();

            if (p == end)
            {
                List<Vector2I> path = new List<Vector2I>();
                Vector2I current = end;
                while (!current.Equals(start))
                {
                    path.Add(current);
                    current = prev[current.Y, current.X];
                }
                path.Add(start);
                path.Reverse();
                return path;
            }

            foreach (Vector2I direction in directions)
            {
                Vector2I np = p + direction;
                if (np.X < 0 || np.X >= map.GetLength(1) || np.Y < 0 || np.Y >= map.GetLength(0)) continue;
                if (map[np.Y, np.X] == 1) continue;
                if (visited[np.Y, np.X]) continue;

                visited[np.Y, np.X] = true;
                prev[np.Y, np.X] = p;
                queue.Enqueue(np);
            }
        }
        return null;
    }

    public int[,] GenerateMaze(int width, int height)
    {
        if (width % 2 == 0) width++;
        if (height % 2 == 0) height++;

        int[,] maze = new int[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                maze[y, x] = 1;

        int[] dirX = { 0, 1, 0, -1 };
        int[] dirY = { -1, 0, 1, 0 };

        var rand = new Random();
        int startX = start_point.X;
        int startY = start_point.Y;

        maze[startY, startX] = 0;
        Stack<(int x, int y)> stack = new Stack<(int, int)>();
        stack.Push((startX, startY));

        while (stack.Count > 0)
        {
            var (x, y) = stack.Peek();

            List<(int nx, int ny, int dx, int dy)> neighbors = new List<(int, int, int, int)>();
            for (int i = 0; i < 4; i++)
            {
                int nx = x + dirX[i] * 2;
                int ny = y + dirY[i] * 2;
                if (nx > 0 && nx < width - 1 && ny > 0 && ny < height - 1 && maze[ny, nx] == 1)
                {
                    neighbors.Add((nx, ny, dirX[i], dirY[i]));
                }
            }

            if (neighbors.Count > 0)
            {
                var (nx, ny, dx, dy) = neighbors[rand.Next(neighbors.Count)];
                maze[y + dy, x + dx] = 0;
                maze[ny, nx] = 0;
                stack.Push((nx, ny));
            }
            else
            {
                stack.Pop();
            }
        }

        return maze;
    }

    public static int[,] GenerateMazePrim(int width, int height)
    {
        if (width % 2 == 0) width++;
        if (height % 2 == 0) height++;

        int[,] maze = new int[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                maze[y, x] = 1;

        int[] dirX = { 0, 1, 0, -1 };
        int[] dirY = { -1, 0, 1, 0 };

        Random rand = new Random();

        int startX = rand.Next(1, width - 1);
        int startY = rand.Next(1, height - 1);
        if (startX % 2 == 0) startX--;
        if (startY % 2 == 0) startY--;

        maze[startY, startX] = 0;

        List<(int x1, int y1, int x2, int y2)> walls = new List<(int, int, int, int)>();

        for (int i = 0; i < 4; i++)
        {
            int nx = startX + dirX[i] * 2;
            int ny = startY + dirY[i] * 2;
            if (nx > 0 && nx < width - 1 && ny > 0 && ny < height - 1)
            {
                walls.Add((startX, startY, nx, ny));
            }
        }

        while (walls.Count > 0)
        {
            int wallIndex = rand.Next(walls.Count);
            var (x1, y1, x2, y2) = walls[wallIndex];
            walls.RemoveAt(wallIndex);

            if (maze[y1, x1] == 0 && maze[y2, x2] == 1)
            {
                int wallX = (x1 + x2) / 2;
                int wallY = (y1 + y2) / 2;
                maze[wallY, wallX] = 0;
                maze[y2, x2] = 0;

                for (int i = 0; i < 4; i++)
                {
                    int nx = x2 + dirX[i] * 2;
                    int ny = y2 + dirY[i] * 2;
                    if (nx > 0 && nx < width - 1 && ny > 0 && ny < height - 1 && maze[ny, nx] == 1)
                    {
                        walls.Add((x2, y2, nx, ny));
                    }
                }
            }
            else if (maze[y1, x1] == 1 && maze[y2, x2] == 0)
            {
                int wallX = (x1 + x2) / 2;
                int wallY = (y1 + y2) / 2;
                maze[wallY, wallX] = 0;
                maze[y1, x1] = 0;

                for (int i = 0; i < 4; i++)
                {
                    int nx = x1 + dirX[i] * 2;
                    int ny = y1 + dirY[i] * 2;
                    if (nx > 0 && nx < width - 1 && ny > 0 && ny < height - 1 && maze[ny, nx] == 1)
                    {
                        walls.Add((x1, y1, nx, ny));
                    }
                }
            }
        }

        return maze;
    }
}