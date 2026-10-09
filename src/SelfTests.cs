using System.Text.Json;

namespace MeiOS;

public static class SelfTests
{
    private static readonly List<object> Results = [];
    private static void Check(string name, bool condition, string actual)
    {
        Results.Add(new { name, passed=condition, actual });
        if (!condition) throw new InvalidOperationException("Тест не пройден: " + name + " / " + actual);
    }
    public static SimulationModel Empty(int quantum=4, int duration=3)
    {
        var config=new ModelConfig { Slots=8, Memory=new(512,512,1), TaskSize=new(64,64,1), Quantum=quantum, AgingTicks=3, LoadTicks=0, DispatchTicks=0, IoInitTicks=0, IoInterruptTicks=0, FinishTicks=0, IoDuration=duration };
        var m=new SimulationModel(config);
        m.Initialize();
        m.AutoLoad=false;
        Array.Clear(m.Table);
        foreach(var q in m.ReadyQueues) q.Clear();
        m.Cpu.Active=null; m.SystemJobs.Clear(); m.Io.Clear();
        return m;
    }
    public static Psw Insert(SimulationModel m, int size, Command[]? commands=null)
    {
        m.SetPendingTask(size,commands);
        if(!m.LoadNewTask()) throw new InvalidOperationException("Ошибка тестовой загрузки");
        return m.Residents.Last();
    }
    public static void Run()
    {
        // Интерфейс: узкие окна, все экраны, длинные строки и границы прокрутки.
        var ui = Empty();
        Insert(ui, 64); Insert(ui, 200);
        ui.Log(new string('Ж', 500));
        foreach (var process in ui.Residents) { process.Id = 12345678 + process.Slot; process.Pc = int.MaxValue; }
        ui.Cpu.Counter = int.MaxValue;
        bool layoutOk = true;
        foreach (int width in new[] { 1, 40, 79, 119, 179 })
        foreach (int height in new[] { 5, 10, 24, 40 })
        for (int view = 0; view < 3; view++)
        {
            int actualScroll = -1;
            var frame = Dashboard.Build(ui, "task " + new string('9', 100), width, height, view, int.MaxValue, true, value => actualScroll = value);
            layoutOk &= frame.Count == height && frame.All(line => line.Length <= width) && actualScroll >= 0 && actualScroll < int.MaxValue;
            if (width >= 5) layoutOk &= frame[^1].EndsWith('9');
            frame = Dashboard.Build(ui, "", width, height, view, -100);
            layoutOk &= frame.Count == height && frame.All(line => line.Length <= width);
        }
        Check("dashboard_layout", layoutOk, "all views, 20 window sizes, long IDs/log/input, scroll limits");
        var m=Empty();
        Insert(m,200); Insert(m,300);
        m.SetPendingTask(20);
        Check("memory_reject",!m.LoadNewTask() && m.FreeMemory==12,$"free={m.FreeMemory}");
        m=Empty();
        for(int i=0;i<8;i++) Insert(m,1);
        m.SetPendingTask(1);
        Check("psw_capacity",!m.LoadNewTask() && m.FreeMemory==504,$"slots={m.Residents.Count()}");
        m=Empty(); var p=Insert(m,64);
        m.Dispatch(); m.ExecuteOneTick();
        Check("hardware_pc",m.Cpu.Counter==1 && p.Pc==1,$"pc={p.Pc}");
        m.ExecuteDirective("speed 1000"); m.ChangeSpeed(1.1);
        Check("speed_max",m.Speed==1000,$"speed={m.Speed}");
        m.ExecuteDirective("speed 0.1"); m.ChangeSpeed(0.9);
        Check("speed_min",m.Speed==0.1,$"speed={m.Speed}");
        m.ExecuteDirective("speed 100"); m.ExecuteDirective("+");
        Check("speed_plus",Math.Abs(m.Speed-110)<1e-9,$"speed={m.Speed}");
        m.ExecuteDirective("speed 100"); m.ExecuteDirective("-");
        Check("speed_minus",Math.Abs(m.Speed-90)<1e-9,$"speed={m.Speed}");
        var a=new SimulationModel(); var b=new SimulationModel(); a.Initialize(); b.Initialize();
        Check("seed_repeat",string.Join('|',a.Residents)==string.Join('|',b.Residents),string.Join('|',a.Residents));
        var computeOnly=Enumerable.Repeat(new Command(Op.Compute),30).Append(new Command(Op.Exit)).ToArray();
        m=Empty(2); var first=Insert(m,64,computeOnly); var second=Insert(m,64,computeOnly); m.Dispatch();
        m.ExecuteOneTick(); m.ExecuteOneTick();
        Check("rr_equal_priority",m.Cpu.Active==second,$"active={m.Cpu.Active?.Id}");
        m=Empty(20); first=Insert(m,64,computeOnly); m.Dispatch(); second=Insert(m,300,computeOnly);
        m.ExecuteOneTick();
        Check("relative_no_preemption",m.Cpu.Active==first,$"active={m.Cpu.Active?.Id}, new_priority={second.Priority}");
        m=Empty(); first=Insert(m,64); second=Insert(m,300);
        Check("large_jobs_priority",m.GetNextProcess()==second,$"base small={first.BasePriority}, large={second.BasePriority}");
        for(int i=0;i<6;i++) m.AgeReadyProcesses();
        Check("aging",first.Priority==2,$"priority={first.Priority}");
        m=Empty(100); p=Insert(m,64,[new(Op.Compute),new(Op.Exit)]);
        m.ExecuteOneTick(); m.ExecuteOneTick();
        Check("compute_exit_memory",p.Memory[2]==5 && m.Completed.Count==1 && m.UsedMemory==0,$"result={p.Memory[2]}, completed={m.Completed.Count}, used={m.UsedMemory}");
        m=Empty(100); p=Insert(m,64,[new(Op.Compute,99),new(Op.Exit)]);
        m.ExecuteOneTick();
        Check("memory_protection",p.State==ProcessState.Killed && m.UsedMemory==0,$"state={p.State}");
        Console.WriteLine(JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
