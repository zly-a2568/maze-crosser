using Godot;
using System;

public partial class Panel : Control
{
	// Called when the node enters the scene tree for the first time.
	private Button resume,exit;

	private CheckButton sound;

	private TextureRect background;

	private TextureRect mask;
	public override void _Ready()
	{
		background=GetNode<TextureRect>("TextureRect");
		resume=GetNode<Button>("V/StopPanel/H/Resume");
		exit=GetNode<Button>("V/StopPanel/H/Exit");
		mask=GetNode<TextureRect>("../TextureRect");
		resume.Connect(Button.SignalName.Pressed,Callable.From(OnResumePressed));
		exit.Connect(Button.SignalName.Pressed,Callable.From(OnExitPressed));
	}

    // Called every frame. 'delta' is the elapsed time since the previous frame.
	public async void HidePanel()
	{
		var t=CreateTween();
		t.SetParallel(true);
		t.TweenProperty(this,"modulate:a",0.0,0.3);
		t.TweenProperty(background.Material,"shader_parameter/blur_amount",0.0,0.3);
		t.TweenProperty(background.Material,"shader_parameter/mix_amount",0.0,0.3);
		await ToSignal(t,Tween.SignalName.Finished);
		Hide();
		GetTree().Paused=false;
	}
	private bool OnResumePressed()
	{
		HidePanel();
		return true;
	}

	private bool OnExitPressed()
	{
		var t=CreateTween();
		t.TweenProperty(this,"modulate:a",0.0,0.3);
		t.Parallel().TweenProperty(background.Material,"shader_parameter/blur_amount",0.0,0.3);
		t.Parallel().TweenProperty(background.Material,"shader_parameter/mix_amount",0.0,0.3);
		t.TweenProperty(mask,"modulate:a",1.0,0.3);
		t.TweenCallback(Callable.From(() =>
		{
			GameProcessor.Instance.ChangeScene("res://scenes/start.tscn");
		}));
		return true;
	}

	public void ShowPanel()
	{
		GetTree().Paused=true;
		this.Modulate=new Color(0xFFFFFF00);
		this.Show();
		var t=CreateTween();
		t.TweenProperty(this,"modulate:a",1.0,0.3);
		var t1=CreateTween();
		t1.TweenProperty(background.Material,"shader_parameter/blur_amount",2.0,0.3);
		t1=CreateTween();
		t1.TweenProperty(background.Material,"shader_parameter/mix_amount",0.3,0.3);
	}
}
