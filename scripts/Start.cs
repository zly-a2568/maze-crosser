using Godot;
using System;
using System.Threading.Tasks;

public partial class Start : Control
{
	// Called when the node enters the scene tree for the first time.

	private Label title;
	private Button start;

	private SelectPanel select;
	public override void _Ready()
	{
		title = GetNode<Label>("V/Title");
		start = GetNode<Button>("V/Start");
		select = GetNode<SelectPanel>("SelectPanel");
		title.VisibleRatio=0.0f;
		title.Modulate= new Color(0xFFFFFF00);
		start.Modulate= new Color(0xFFFFFF00);
		var t=CreateTween();
		t.TweenProperty(title,"visible_ratio",1.0f,0.5f).SetTrans(Tween.TransitionType.Cubic);
		t.TweenProperty(title,"modulate",new Color(0xFFFFFFFF),0.5f).SetTrans(Tween.TransitionType.Cubic);
		t.TweenInterval(0.5f);
		t.TweenProperty(start,"modulate",new Color(0xFFFFFFFF),0.5f).SetTrans(Tween.TransitionType.Cubic);
		start.Connect("pressed",Callable.From(OnStart));
		select.Connect(SelectPanel.SignalName.Canceled,Callable.From(OnCancel));
	}

	private void OnCancel()
	{
		title.Show();
		start.Show();
		title.VisibleRatio=0.0f;
		var t=CreateTween();
		t.TweenProperty(title,"visible_ratio",1.0f,0.5f).SetTrans(Tween.TransitionType.Cubic);
		t.TweenProperty(title,"modulate",new Color(0xFFFFFFFF),0.5f).SetTrans(Tween.TransitionType.Cubic);
		t.TweenInterval(0.5f);
		t.TweenProperty(start,"modulate",new Color(0xFFFFFFFF),0.5f).SetTrans(Tween.TransitionType.Cubic);
	}

	private void OnStart()
	{
		var t=CreateTween();
		t.TweenProperty(title,"visible_ratio",0.0f,0.5f);
		t.TweenProperty(title,"modulate",new Color(0xFFFFFF00),0.5f);
		t.TweenProperty(start,"modulate",new Color(0xFFFFFF00),0.5f);
		t.TweenInterval(0.5f);
		t.TweenCallback(Callable.From(()=>{
			title.Hide();
			start.Hide();
			select.ShowPanel();}));
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
