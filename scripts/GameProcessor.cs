using Godot;
using System;

public partial class GameProcessor : Node
{
	// Called when the node enters the scene tree for the first time.
	public static GameProcessor Instance {get ; private set;}

	public Vector2I map_size;
	public override void _Ready()
	{
		Instance=this;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void ChangeScene(string path)
	{
		var current=GetTree().CurrentScene;
		current.Free();
		var nextScene=GD.Load<PackedScene>(path).Instantiate();
		GetTree().Root.AddChild(nextScene);
		GetTree().CurrentScene=nextScene;
	}
}
