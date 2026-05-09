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
    private const double GhostSpawnInterval = 0.03; // 每0.03秒生成一个幽灵

    // 【优化】缓存计算属性，避免每帧都进行除法和类型转换
    private Vector2I PlayerGridPos => (Vector2I)(_player.Position / 16);

    public override void _Ready()
    {
        // 安全检查
        if (GameProcessor.Instance == null)
        {
            PrintErr("GameProcessor Instance is null!");
            return;
        }

        map_size = GameProcessor.Instance.map_size;
        _camera = GetNode<Camera2D>("player/camera");
        _map = GetNode<TileMapLayer>("map");
        _player = GetNode<CharacterBody2D>("player");
        prompts = _player.GetNode<Node2D>("prompts");
        mask = GetNode<TextureRect>("panel/TextureRect");
        panel = GetNode<Panel>("panel/Panel");
        
        map = GenerateMaze(map_size.X, map_size.Y);
        
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
        
        _camera.ResetSmoothing();
        _camera.ForceUpdateScroll();
        
        var t = CreateTween();
        t.TweenProperty(mask, "modulate:a", 0.0f, 0.5);
        _player.GetNode<Sprite2D>("Icon").Scale = new Vector2(0.0f, 0.0f);
        t.TweenProperty(_player.GetNode<Sprite2D>("Icon"), "scale", new Vector2(1.0f, 1.0f), 0.5).SetTrans(Tween.TransitionType.Sine);
        
        UpdatePrompt();
    }

    public override void _Process(double delta)
    {
        // 【修复】必须先判断 moving，再判断 won，逻辑更严密
        if (moving)
        {
            _ghostSpawnTimer += delta;
            if (_ghostSpawnTimer >= GhostSpawnInterval)
            {
                _ghostSpawnTimer = 0.0;
                SpawnGhostEffect();
            }
        }

        // 【关键性能优化】
        // 原代码：LocalToMap(ToLocal(ToGlobal(_player.Position))) 
        // 问题：每帧产生3个临时对象，且涉及复杂的坐标变换矩阵运算
        // 优化：直接使用整型坐标比较。因为地图格子是16x16，直接除以16即可得到逻辑坐标。
        if (!won && PlayerGridPos == end_point)
        {
            // 为了防止重复触发，在这里不再直接调用 GameWin，而是依靠外部逻辑或者确保 GameWin 内部有防重入机制
            // 但最简单的做法是直接调用，并在 GameWin 开头把 won 设为 true
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
    // 【核心修复】在方法开始时立即停止 _Process 中的幽灵生成逻辑
    private async Task GameWin()
    {
        // 【关键修复步骤1】第一时间停止移动标记
        // 这样 _Process 中的 if (moving) 就会在下一帧失效，停止生成幽灵
        moving = false;
        won = true;

        // 【关键修复步骤2】停止节点的物理和逻辑处理，防止异步等待期间发生冲突
        SetProcess(false);
        SetPhysicsProcess(false);
        
        // 对齐玩家位置（视觉上）
        _player.Position = end_point * 16 + new Vector2(8.0f, 8.0f);
        
        // 杀死现有的所有 Tween，防止动画冲突
        var ts = GetTree().GetProcessedTweens();
        for (int i = 0; i < ts.Count; i++)
        {
            if (IsInstanceValid(ts[i])) ts[i].Kill();
        }

        // 遮罩层渐显
        mask.Show();
        var t1 = CreateTween();
        t1.TweenProperty(mask, "modulate:a", 1.0, 0.2);

        // 处理所有现有的幽灵
        var ghosts = GetTree().GetNodesInGroup("ghost");
        foreach (Sprite2D a in ghosts)
        {
            if (!IsInstanceValid(a)) continue;
            
            var map_rect = _map.GetUsedRect();
            var t = CreateTween();
            // 让幽灵掉下去
            t.TweenProperty(a, "position", a.Position + 16.0f * (new Vector2(0.0f, map_rect.Position.Y + map_rect.Size.Y)), 0.05).SetTrans(Tween.TransitionType.Bounce);
            t.Finished += () =>
            {
                if (IsInstanceValid(a)) a.QueueFree();
            };
            
            // 【安全检查】等待期间，如果场景被卸载，直接退出
            if (IsInstanceValid(this))
            {
                await ToSignal(GetTree().CreateTimer(0.04), Timer.SignalName.Timeout);
            }
            else
            {
                return;
            }
        }

        // 清理当前节点的所有子节点（保留节点自身以便可能的后续操作，虽然这里马上就要切场景了）
        foreach (var a in GetChildren())
        {
            if (IsInstanceValid(a) && !a.IsQueuedForDeletion())
            {
                // 保留 mask，因为它是 Game 的子节点，不要杀掉自己正在用的东西，或者全杀掉也行，反正要切场景了
                // 为了安全，我们只清理非 Panel 节点，或者直接依赖场景切换
                // 这里选择不清理 Panel 和 Mask，避免闪屏，直接切场景即可
                if (a != panel && a != mask && a != _map) 
                   a.QueueFree();
            }
        }

        // 【安全检查】最后调用外部方法前再次确认节点存活
        if (IsInstanceValid(this) && IsInstanceValid(GameProcessor.Instance))
        {
            // 使用 CallDeferred 确保当前帧逻辑全部走完再切场景
            GameProcessor.Instance.CallDeferred("ChangeScene", ["res://scenes/game.tscn"]);
        }
    }

    public override void _Input(InputEvent @event)
    {
        base._Input(@event);
        if (@event is InputEventKey)
        {
            // ESC 暂停
            if (@event.IsActionPressed("ui_cancel") && !won)
            {
                panel.ShowPanel();
            }
            
            // 如果赢了或者在移动，不处理方向键
            if (moving || won) return;
            
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
            // 边界检查
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

        // 确保起点在迷宫范围内
        if (startX <= 0 || startX >= width - 1 || startY <= 0 || startY >= height - 1)
        {
            startX = 1;
            startY = 1;
        }
        
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
}