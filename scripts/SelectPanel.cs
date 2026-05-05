using Godot;
using System;

public partial class SelectPanel : Control
{
	// Called when the node enters the scene tree for the first time.
	private Button next,cancel;
	[Signal]
	public delegate void CanceledEventHandler();
	public override void _Ready()
	{
		next=GetNode<Button>("V/H/Next");
		cancel=GetNode<Button>("V/H/Cancel");
		next.Connect("pressed",Callable.From(OnNext));
		cancel.Connect("pressed",Callable.From(OnCancel));
	}
	public void OnNext()
	{
		var height=GetNode<SpinBox>("V/Panel/Grid/SpinBox2").Value;
		var width=GetNode<SpinBox>("V/Panel/Grid/SpinBox").Value;
		GameProcessor.Instance.map_size=new Vector2I((int)width,(int)height);
		var t=CreateTween();
		t.TweenProperty(this,"modulate",new Color(0xFFFFFF00),0.2f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		t.TweenCallback(Callable.From(() =>
		{
			Hide();
			GameProcessor.Instance.ChangeScene("res://scenes/game.tscn");
		}));
	}
	public void OnCancel()
	{
		var t=CreateTween();
		t.TweenProperty(this,"modulate",new Color(0xFFFFFF00),0.2f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		t.TweenCallback(Callable.From(() =>
		{
			Hide();
			EmitSignal(SelectPanel.SignalName.Canceled);
		}));
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void ShowPanel()
	{
		Modulate=new Color(0xFFFFFF00);
		Show();
		var t=CreateTween();
		t.TweenProperty(this,"modulate",new Color(0xFFFFFFFF),0.2f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
	}
}
