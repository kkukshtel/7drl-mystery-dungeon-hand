using Zinc;
using MDH;

Engine.Run(new Engine.RunOptions(1920,1080,"mdh",() =>
	{
		var scene = new Dungeon();
		scene.Mount(0);
		scene.Load(() => scene.Start());
	}
));