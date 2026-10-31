using Online.Protocol;

namespace Protocol.Tests;

/// <summary>The written forms of piconet-docs research/game-modes/ (vocabulary.md, language.md) against the compiler,
/// word by word: each form the documents give compiles, each they refuse is refused, and a word they mark "not built" is
/// refused, so building one fails here until the documents say so too.</summary>
public sealed class ModeWordFormsTests
{
    private static ModeCompilation Compile(string body) => ModeCompiler.Compile($"mode \"X\" {{ api 1; }}\n{body}\n");

    private static string Rule(string body) => $"rule \"R\" {{ event each seat; while race.started; do {{ {body} }} }}";

    [Theory]
    [InlineData("course \"Mute City: Sonic Oval\";")]
    [InlineData("laps unlimited;")]
    [InlineData("racers 2..8;")]
    [InlineData("machines all;")]
    [InlineData("machines [\"Blue Falcon\"];")]
    [InlineData("machine_setting 20..80;")]
    [InlineData("stats \"GX+\";")]
    [InlineData("stats stock;")]
    [InlineData("mixed_packs on;")]
    [InlineData("boost_start off;")]
    [InlineData("teams 2..4;")]
    [InlineData("customs off;")]
    [InlineData("contact off;")]
    [InlineData("restore on;")]
    [InlineData("finish_limit 90;")]
    [InlineData("mine_regen 200;")]
    [InlineData("mine_regen on;")]
    [InlineData("mine_regen off;")]
    [InlineData("cpu_level 2;")]
    [InlineData("pedals locked accel 0.6 brake 0.0;")]
    [InlineData("clock_limit 5min;")]
    [InlineData("rolling_start 1000 km/h;")]
    [InlineData("start_together;")]
    [InlineData("start_together; contact off;")]
    [InlineData("param c \"C\" switch default on; cpus c;")]
    [InlineData("param l \"L\" delay 1min..10min step 30s default 3min; clock_limit l;")]
    [InlineData("results_title \"ABCDEFGHIJKLMNOPQRSTUVW\";")]
    [InlineData("pause off;")]
    [InlineData("solo on; rewind 1min; pause on; save_state on; resume_countdown 1s;")]
    [InlineData("lives 3;")]
    [InlineData("pits_off;")]
    [InlineData("every(10s) { end_race; }")]
    [InlineData("at(4min) { end_race; }")]
    public void ASetupFormTheDocumentsGiveCompiles(string body)
    {
        var result = Compile(body);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("seat.place = 1;")]
    [InlineData("show_number seat, -1;")]
    [InlineData("danger_zone seat, 500.0, 800.0, 3;")]
    [InlineData("danger_zone seat off;")]
    [InlineData("respawns seat off;")]
    [InlineData("ghost seat off;")]
    [InlineData("no contact seats, seats;")]
    [InlineData("team seat.team no contact;")]
    [InlineData("map_hide seat from seats - seat;")]
    [InlineData("map_mark seat, -1;")]
    [InlineData("tag seat, \"BOMB\";")]
    [InlineData("tag seat off;")]
    [InlineData("stock_banner seat, TICK;")]
    [InlineData("show seat, TIME_OVER;")]
    [InlineData("hide seat, FACES;")]
    [InlineData("seat.look = ghost;")]
    [InlineData("seat.own_look = normal;")]
    [InlineData("seat.colour = 0xFF0000FF;")]
    [InlineData("push seat, seat.forward;")]
    [InlineData("seat.velocity = seat.forward;")]
    [InlineData("seat.energy -= 1.0;")]
    [InlineData("show_clock 600;")]
    [InlineData("break_down seat;")]
    [InlineData("int a = random(5); real b = random_real();")]
    [InlineData("int a = 2 × 3;")]
    [InlineData("real a = 2.;")]
    [InlineData("for each s in seats by standing desc { retire s; }")]
    [InlineData("seat s = nth(seat.touching, 0); bool b = seat in seat.touching;")]
    public void AnActionFormTheDocumentsGiveCompiles(string body)
    {
        var result = Compile(Rule(body));
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("overtake(a, b)")]
    [InlineData("respawned(s)")]
    [InlineData("land(s)")]
    [InlineData("boost_end(s)")]
    [InlineData("wrong_way(s)")]
    [InlineData("out(s, cause)")]
    [InlineData("ko(v, by)")]
    [InlineData("hit(a, b)")]
    public void AnEventBindsItsNames(string form)
    {
        Assert.Empty(Compile($"rule \"R\" {{ event {form}; do {{ }} }}").Errors);
    }

    [Theory]
    [InlineData("boost;")]
    [InlineData("boost 1;")]
    [InlineData("boost true;")]
    [InlineData("finish_limit 90s;")]
    [InlineData("mine_regen 200ms;")]
    [InlineData("laps 100;")]
    [InlineData("racers 0..8;")]
    [InlineData("laps 3; laps by course { else: 3; }")]
    [InlineData("pause on;")]
    [InlineData("solo on; cpus on;")]
    [InlineData("solo on; save_state on;")]
    [InlineData("results points;")]
    [InlineData("race_waits everyone;")]
    [InlineData("results_title \"ABCDEFGHIJKLMNOPQRSTUVWX\";")]
    [InlineData("results_title \"Low\";")]
    [InlineData("results_title \" A\";")]
    [InlineData("clock_limit 600; rule \"R\" { event go; do { show_clock 5; } }")]
    [InlineData("rule \"R\" { event go; while race.started; do { } }")]
    [InlineData("global vec v;")]
    [InlineData("global int series;")]
    [InlineData("global int race;")]
    [InlineData("macro m() { }")]
    [InlineData("seat int z; rule \"R\" { event go; do { for each seat by z { retire seat; } } }")]
    [InlineData("rule \"R\" { event each seat; do { int a = 2 − 3; } }")]
    public void AFormTheDocumentsRefuseIsRefused(string body)
    {
        Assert.NotEmpty(Compile(body).Errors);
    }

    [Theory]
    [InlineData("damage 200%; knockback 300%; wall_damage 50%; wall_bounce 1000%; spin_attacks off; side_attacks off; spin_immunity on;")]
    [InlineData("param p \"D\" number 0.5..2.0 default 1.0; damage p;")]
    [InlineData("rule \"R\" { event go; do { race.damage = 2.0; race.knockback *= 2; race.spin_attacks = off; race.side_attacks = not race.side_attacks; race.spin_immunity = on; race.wall_damage = 50%; race.wall_bounce += 1; } }")]
    [InlineData("time_bonus off;")]
    [InlineData("clock_limit 2min; time_bonus on;")]
    [InlineData("clock_limit 2min; time_bonus 10;")]
    [InlineData("clock_limit 2min; points_table game;")]
    [InlineData("points_table [100, 80, 60]; rule \"R\" { event go; do { end_race; } }")]
    [InlineData("clock_limit 2min; points_table game; results points;")]
    public void ACombatOrPointsWordCompiles(string body)
    {
        Assert.Empty(Compile(body).Errors);
    }

    [Theory]
    [InlineData("damage 5%;")]
    [InlineData("knockback 1001%;")]
    [InlineData("spin_attacks 2;")]
    [InlineData("time_bonus on;")]
    [InlineData("points_table game;")]
    [InlineData("clock_limit 2min; points_table [];")]
    [InlineData("clock_limit 2min; points_table [1000];")]
    [InlineData("rule \"R\" { event go; do { race.steps = 1; } }")]
    [InlineData("rule \"R\" { event go; do { race.spin_attacks = 1.0; } }")]
    public void ACombatOrPointsFormIsRefused(string body)
    {
        Assert.NotEmpty(Compile(body).Errors);
    }

    [Theory]
    [InlineData("start_together; contact on;")]
    [InlineData("param c \"C\" switch default off; start_together; contact c;")]
    public void StartTogetherRefusesAContactThatCanBeOn(string body)
    {
        var error = Assert.Single(Compile(body).Errors);
        Assert.Contains("start_together", error.ToString());
    }

    /// <summary>The design's example 9.3, as it stands there: inside the budget by squares, with no square root in its loop.</summary>
    [Fact]
    public void ChainReactionCompiles()
    {
        var result = ModeCompiler.Compile("""
            mode "Chain reaction" { api 1; badge "CHAIN"; about "An explosion takes everyone near it."; }
            param radius "Blast radius" number 10..200 default 60;      // metres

            rule "Blast" {
              event destroyed(s);                                       // the explosion path, not a retire
              do {
                vec at = s.position;
                real reach = radius * radius;                           // squares: no square root in a loop of 30
                for each seat where racing and seat != s {
                  vec apart = seat.position - at;
                  if (dot(apart, apart) <= reach) { explode seat; }
                }
              }
            }
            """);
        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
    }

    /// <summary>The design's example 9.5, as it stands there.</summary>
    [Fact]
    public void InfectionCompiles()
    {
        var result = ModeCompiler.Compile("""
            mode "Infection" { api 1; badge "INFECTION"; about "A touch passes it on. Stay clean."; }
            param slow  "Slowdown at half a lap" number 0..50 default 20;
            param limit "Time" delay 1min..10min step 30s default 3min;

            laps unlimited;
            clock_limit limit;
            seat int infected_at;

            rule "Patient zero" {
              event go;
              do {
                seat z = nth(seats where racing, random(count(seats where racing)));
                z.infected_at = 1;
              }
            }

            rule "Spread" {
              event hit(a, b);
              do {
                if (a.infected_at == 0) { if (b.infected_at > 0) { a.infected_at = race.steps; } }
                else if (b.infected_at == 0) { b.infected_at = race.steps; }
              }
            }

            rule "Hide and slow" {
              event each seat;
              while race.started and seat.racing and seat.infected_at == 0;
              do {
                real d = min(gap_to(seat, other) for each other where other.infected_at > 0 and other.racing);
                real f = min(1.0, d / (seat.lap_length / 2));
                seat.stat.max_speed = seat.stat.base.max_speed * (1 - slow / 100.0 * f);
              }
            }

            rule "Map" {
              event every step;
              do {
                seats clean = (seats where seat.infected_at == 0);
                for each seat where seat.infected_at > 0 { map_hide seat from clean; }
              }
            }

            rule "Places at the end" {
              event every step;
              when race.started and race.steps >= limit;
              do {
                int n = 1;
                for each seat where seat.infected_at == 0 by standing { seat.place = n; n += 1; }
                for each seat where seat.infected_at > 0 by seat.infected_at desc { seat.place = n; n += 1; }
              }
            }
            """);
        Assert.Empty(result.Errors);
        Assert.InRange(result.Budget, 1, ModeWords.Budget);
    }

    [Theory]
    [InlineData("rule \"R\" { event each seat; do { damage seat, 5.0; } }")]
    [InlineData("rule \"R\" { event each seat; do { real a = seat.stat.max_speed; } }")]
    [InlineData("rule \"R\" { event each seat; do { real a = seat.stat.base.body; } }")]
    [InlineData("rule \"R\" { event each seat; do { seat.stat.max_speed = 1.0; } }")]
    [InlineData("rule \"R\" { event each seat; do { seat.stat.drag *= 2; } }")]
    [InlineData("rule \"R\" { event go; do { seat a = first(seats); a.stat.boost_length -= 0.5; } }")]
    [InlineData("rule \"R\" { event go; do { map_hide nth(seats, 0) from (seats where racing); } }")]
    [InlineData("rule \"R\" { event go; do { for each s in (seats where racing) { retire s; } } }")]
    [InlineData("rule \"R\" { event go; do { int a = count((seats where racing) - nth(seats, 0)); } }")]
    public void AStatWordOrASetValueCompiles(string body)
    {
        Assert.Empty(Compile(body).Errors);
    }

    [Theory]
    [InlineData("rule \"R\" { event each seat; do { seat.stat.base.body = 1.0; } }", "the machine's own")]
    [InlineData("rule \"R\" { event each seat; do { seat.stat.colour = 1.0; } }", "no stat")]
    [InlineData("rule \"R\" { event each seat; do { real a = seat.stat.colour; } }", "no stat")]
    [InlineData("rule \"R\" { event each seat; do { seat.stat.base.colour = 1.0; } }", "the machine's own")]
    [InlineData("rule \"R\" { event each seat; do { real a = seat.stat.base.colour; } }", "no stat")]
    public void AStatWordIsRefusedWithItsReason(string body, string reason)
    {
        Assert.Contains(Compile(body).Errors, e => e.Contains(reason, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("rule \"R\" { event each seat; do { show_number seat off; } }")]
    [InlineData("rule \"R\" { event go; do { int a = race.humans_left; } }")]
    public void AWordTheDocumentsMarkNotBuiltIsRefused(string body)
    {
        Assert.NotEmpty(Compile(body).Errors);
    }

    [Theory]
    [InlineData("rule \"R\" { event each seat; when seat.racing; do { seat.speed = 900; } }", true)]
    [InlineData("rule \"R\" { event each seat; when seat.racing; do { seat.velocity = seat.forward; } }", true)]
    [InlineData("rule \"R\" { event each seat; when seat.racing; do { show_number seat, 3; } }", true)]
    [InlineData("rule \"R\" { event every step; when race.started; do { show_clock 60; } }", true)]
    [InlineData("global int x; rule \"R\" { event every step; when race.started; do { x = race.steps; } }", false)]
    [InlineData("global int x; rule \"R\" { event every step; when race.started; do { x = race.steps + 60; } }", false)]
    [InlineData("rule \"R\" { event each seat; when seat.racing; do { stock_banner seat, TICK; } }", false)]
    [InlineData("rule \"R\" { event each seat; when seat.racing; do { hide seat, RANK; } }", false)]
    [InlineData("rule \"R\" { event each seat; while seat.racing; do { seat.speed = 900; } }", false)]
    [InlineData("rule \"R\" { event lap(s); when s.racing; do { s.speed = 900; } }", false)]
    [InlineData("rule \"R\" { event each seat; while seat.racing; do { show_number seat, 3; } }", false)]
    [InlineData("rule \"R\" { event each seat; when seat.racing; while seat.racing; do { show_number seat, 3; } }", false)]
    [InlineData("rule \"R\" { event each seat; when seat.racing; do { retire seat; } }", false)]
    [InlineData("rule \"R\" { event out(s); when s.racing; do { show_number s, 3; } }", false)]
    public void AWhenWithAPerStepWordWarns(string body, bool warns)
    {
        var result = Compile(body);
        Assert.Empty(result.Errors);
        Assert.Equal(warns, result.Warnings.Any(w => w.Contains("did you mean while", StringComparison.Ordinal)));
    }

    /// <summary>The library's lives (design 9.6): it counts on destroyed, not on every restore, and carries the count on
    /// the spare-machine icon. Its own file needs no show_number or destroyed rule.</summary>
    [Fact]
    public void LivesCountsDestroysAndShowsTheCount()
    {
        var listing = ModeDisassembler.List(Compile("lives 3;").Package!);
        Assert.Contains("show_number", listing, StringComparison.Ordinal);
        Assert.Contains("destroyed", listing, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rule \"R\" { event each seat; do { push seat, vec(100.0, 0, seat.speed); } }")]
    [InlineData("rule \"R\" { event each seat; do { vec v = vec(1, 2, 3); seat.velocity = v * 2; } }")]
    [InlineData("param force \"F\" number 0..500 default 100; rule \"R\" { event each seat; do { push seat, cross(seat.forward, seat.up) * force; } }")]
    [InlineData("rule \"R\" { event each seat; do { real a = cross(seat.up, vec(0, 1, 0)).y; } }")]
    public void AVecIsBuilt(string body)
    {
        Assert.Empty(Compile(body).Errors);
    }

    [Theory]
    [InlineData("rule \"R\" { event each seat; do { vec v = vec(1, 2); } }", "three numbers")]
    [InlineData("rule \"R\" { event each seat; do { vec v = vec(seat, 2, 3); } }", "vec takes")]
    [InlineData("rule \"R\" { event each seat; do { vec v = cross(seat.up); } }", "two vecs")]
    [InlineData("rule \"R\" { event each seat; do { vec v = cross(seat.up, 1.0); } }", "vec")]
    public void AVecFormIsRefused(string body, string reason)
    {
        Assert.Contains(Compile(body).Errors, e => e.Contains(reason, StringComparison.Ordinal));
    }

    /// <summary>The docs' <c>length</c> and <c>dot</c>, and a vec's arithmetic (language 4).</summary>
    [Theory]
    [InlineData("global real x; rule \"R\" { event each seat; do { x = length(seat.velocity); } }")]
    [InlineData("global real x; rule \"R\" { event each seat; do { x = dot(seat.forward, seat.velocity); } }")]
    [InlineData("global real x; rule \"R\" { event each seat; do { x = length(seat.position - seat.up * 2); } }")]
    public void VecHelpersCompile(string body)
    {
        Assert.Empty(Compile(body).Errors);
    }

    /// <summary>Design 9.3, Chain reaction: the budget refuses it as written. The design says so; a change of the budget or
    /// the costs fails here until it does not.</summary>
    [Fact]
    public void TheDesignsChainReactionIsPastTheBudget()
    {
        var result = Compile("""
            param radius "Blast radius" number 10..200 default 60;
            rule "Blast" { event destroyed(s); do { for each seat where racing and seat != s and dist(seat, s) <= radius { explode seat; } } }
            """);
        Assert.Contains(result.Errors, e => e.Contains("past api 1's 65536", StringComparison.Ordinal));
    }
}
