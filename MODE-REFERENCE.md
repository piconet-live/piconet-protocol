# Mode reference

A `.fzmode` file is a UTF-8 text file that sets the rules of a custom lobby's race. This is the vocabulary of `api 0.6.1`.
A file is at most 64 KB, and a word it leaves out is the game's own way.

## The api line

A file names the game version it was written for: `api 0.6.1;`. That version's rules are frozen once it is released,
so the file means the same in every later version of the game. Players on an older version than the file names are
asked to update before they can race it.

| Written | Means |
|---|---|
| `api 0.6.1;` | The rules of Piconet 0.6.1 |
| `api 0.6.0;` or `api 1;` | The rules of Piconet 0.6.0 (its files wrote `api 1`, which stays valid) |

- A word or value that arrived later than the file's version is refused, with the version it needs: `cpus always
  needs api 0.6.1: this file says api 1 (0.6.0)`. Raise the api line to use it.
- A version that changed no rule has the rules of the one before it.
- A file that names a version newer than the game is refused: `api 0.7.0 is newer than this build, which knows up to
  api 0.6.1: update to use this file`.

| Arrived in | Words and values |
|---|---|
| 0.6.1 | `cpus always` |
| 0.6.0 | Everything else on this page |

## What goes in a file

| Part | What it does | Looks like |
|---|---|---|
| Header | First in the file: the mode's name, the api (required), a short badge and a one-line about (both optional) | `mode "Knockout" { api 0.6.1; badge "KNOCKOUT"; about "…"; }` |
| Parameter | A setting the host changes in the lobby; a constant in the rules | `param keep "Racers left" number 1..29 default 1;` |
| Setup word | Fixes a rule of the race before it starts; each written once | `laps 5;` |
| Constant | Fixed when the file compiles | `const int keep = 8;` |
| Global variable | One for the race | `global int cuts;` |
| Seat variable | One for every seat, read as `s.NAME` | `seat int cut_order;` |
| Team variable | One for every team, read as `s.team.NAME` | `team real pool;` |
| Rule | Code that runs at an event | `rule "Label" { event lap(s); when …; do { … } }` |
| Sub | Named code with arguments, which may give a value | `sub real lead(seat a, seat b) { … }` |
| Library macro | The standard library's, at the top level; a file cannot declare macros | `lives 3;` `every(30s) { … }` |
| Board line | A variable shown on the launcher's race page, beside the race; nothing is drawn in the game. With a list of words, the value picks the word, 0 the first: an empty word shows nothing, a value past the list shows the number. Not for a vec; a real cannot have words | `board seat cut_order "Out";` `board seat state "State" ["Racing", "Safe", "Out"];` |
| Comment | To the end of the line; or a block, not nested | `// …` `/* … */` |

After the header, parts come in any order. A name is declared above the line that uses it; subs may be called from
anywhere.

### Parameters

| Kind | Written | Read in a rule as |
|---|---|---|
| Number | `param n "Label" number 1..9 step 1 default 3;` | an `int`; a `real` when any of its numbers is written as one (`0.5..2.0`, `100%..400%`) |
| Delay | `param t "Label" delay 10s..2min step 5s default 30s;` | an `int`: steps. Every number of the line is a time |
| Switch | `param p "Label" switch default on;` | a `bool`. The default is required |
| Choice | `param c "Label" choice ["First", "Second"] default "First";` | an `int`: the choice's place, 0 for the first |
| Named value | `or "Name"` after the range, `switch` or list: `number 1..99 or "By course" default "By course"` | One value more that states nothing: a setup word given it is as if not written. Taken only by `laps`, `laps by course`, `cpu_level`, `finish_limit`, `mine_regen`, `racers`, `teams`, `machine_setting`, `stats`, `mixed_packs`, `boost_start` and `customs`. A rule cannot read such a parameter; a real range cannot have one |

- `step` and `default` are optional. Without `default`, a number or delay starts at its least and a choice at its
  first name. A whole-number or delay default lies on a step from the least.
- A setup word checks a parameter's whole range against its own. A word that takes a real (`damage`, `rolling_start`,
  `start_gap`, `pedals locked`) needs a real parameter: `500.0..2000.0`, not `500..2000`. A whole-number word takes a
  `number`, a `delay` or a `choice`; an on-or-off word takes a `switch` (`cpus`, which has a third value, also a
  `choice` of three).

### Types and values

| Type | Values |
|---|---|
| `int` | A 32-bit whole number. `+`, `-` and `*` wrap round; `/` drops the fraction toward zero (`7 / 2` is 3); `%` is the remainder, with the left side's sign; dividing by 0 gives 0 |
| `real` | A 32-bit floating-point number, each `+ - * /` rounded once. A calculation with no answer (`0.0 / 0.0`) compares false with everything, and written to the game it ends the race |
| `bool` | `true` or `false`; `on` and `off` are the same |
| `seat` | A racer of this race, player or CPU: 0 to 29, or `none`. A read of `none` gives 0, `false` or `none`; an action on it does nothing |
| `seats` | A set of seats. `seats` alone is every seat; `+` union, `-` difference, `&` intersection, `s in SET`; `(SET where c)` a set by a condition |
| `vec` | Three reals, `.x`, `.y`, `.z`: a position, direction or motion. Locals only |

| Written | Is |
|---|---|
| `42` `0x1F` | Whole numbers; hex at most 8 digits |
| `1.5` `2.` | Reals; a digit before the point is required (`.5` is refused) |
| `50%` | The real 0.5 |
| `30s` `500ms` `2min` `90 steps` | Times, in whole steps, 60 a second. A time between steps is rounded to the nearest, with a warning |
| `1000 km/h` `300 m` | The reals 1000.0 and 300.0; the space is required |
| `"…"` | A string, on one line; `\"` and `\\` the only escapes. Labels and names only: a mode cannot build or compare text |
| `*` or `×` | Multiply. Minus is `-`; `−` is refused |
| Names | A letter or `_`, then letters, digits and `_`; case matters |
| Keywords | `mode api badge about param number delay switch choice default step const global seat team int real bool seats sub rule event every each when while do board if else for in where by desc repeat wait abort restart call return and or not true false none on off laps course macro race vec series carry`; none may name anything. `series` and `carry` are reserved |

Units: speeds in km/h; distances in metres; times in steps unless a word says seconds or milliseconds; energy as the
game counts it, 100 a full machine.

## How rules run

| Written | Runs |
|---|---|
| `event every step;` | Once a step |
| `event each seat;` | Once a step for every seat, in seat order, with `seat` that seat |
| `event lap(s);`, and every other event | Once each time it happens. The names in brackets are yours and bind its seats and values; a seat event written without them binds `seat` |
| `when c;` | With `every step` or `each seat`: once, on the step c becomes true, and not again until it has been false (for `each seat`, seat by seat). With another event: the event counts only while c holds. Several must all hold |
| `while c;` | On every step c holds. Only with `every step` and `each seat` |
| `when` and `while` | On `when`'s step, if `while` holds then |
| Neither | `every step` and `each seat`: every step |
| `event game_start;` | Once, before the first picture with the machines in it: for looks, HUD parts, numbers shown, energy, contact, restores and stats. `wait` is refused, and so are the words that need a running step: `seat.speed`, `seat.velocity`, `push`, `boost`, `retire`, `explode`, `break_down`, `damage`, `finish_seat`, `cpu_mode`, `end_race`, `stop_all`, `stock_banner` |
| `event countdown;` | The first step the machines run: the 3-2-1 begins (with `rolling_start`, at once) |
| `event race_start;` | GO. From then `race.started` is true and `race.steps` counts up from 0 |

| Within a step | |
|---|---|
| Order | 1. The events are found against the last step. 2. They run by kind, in this order: `countdown`, `race_start`, `out`, `ko`, `destroyed`, `fall`, `hit`, `damaged`, `wall`, `boost`, `pit`, `checkpoint`, `lap`, `finish`, `overtake`, `respawned`, `land`, `boost_end`, `wrong_way`; within a kind by seat number (a pair by its lower seat), each with its rules in file order. 3. Rules whose `wait` is over, in rule order, then seat order. 4. The `every step` and `each seat` rules, in file order. 5. The actions are applied |
| Reads | The game as it stood when the step began, for the whole step: after `seat.energy = 50.0;`, `seat.energy` reads the old value until the next step. The same for `seat.points` after `award` and `seat.place` after `seat.place = n` |
| Variables | The mode's own change at once |
| Actions | Queued and applied after every rule, in order, seen from the next step; of two writes to one thing, the last wins. `end_race` comes after every other action of its step |
| Displays | Not queued |
| `seat.speed`, `seat.velocity`, `show_clock`, `show_number` | Hold only while written: put them in a `while` rule. In a `when` rule they compile with a warning |

### Statements

| Written | What it does |
|---|---|
| `x = e;` `x += e;` `x -= e;` `x *= e;` `x /= e;` | Assigns |
| `TYPE name = e;` | A local, until its block ends. Its name may not be one the file declares |
| `if (c) { … } else if (c) { … } else { … }` | Braces are always required |
| `for each seat where c by key desc, key { … }` | The seats in seat order, binding `seat`; `where` skips seats; `by` sorts, smallest first unless `desc`, ties to the next key and last to the seat number, `false` before `true`. `where` and `by` are optional; at most 8 keys |
| `for each s in SET where c { … }` | Only SET's seats, with a name of your own |
| `repeat N { … }` | N times; N a whole-number constant or parameter |
| `wait T;` | Stops the rule here for T steps, its locals kept; 0 or less does nothing. Refused in `game_start`, inside a sorted loop and in a sub used in an expression |
| `wait T abort when c;` | The rest of the rule is dropped if c becomes true during the wait |
| `wait T restart when c;` | The wait starts again on every step c is true |
| `call name(a, b);` | Runs a sub |
| `return e;` `return;` | Leaves a sub, with its value or none |

- Inside `where`, `by` and a set helper's brackets, a read alone means the loop's seat: `for each seat where racing`.
  In a body, write `seat.racing`.
- Loops over the seats nest two deep (a set helper is one); sorted loops and helpers nest two deep. There is no `while`
  loop.
- A waiting rule does not run again for that seat until its wait is over.
- A sub sees its arguments, its locals and the file's names. It is written out where it is called, and may not call
  itself, directly or through another.

## Setup words

- A word is written once. A word with two forms takes one, except beside a parameter with a named value:
  `laps by course { … }` with `laps lap_count;` races the table at "By course" and the number otherwise.
- An on-or-off word takes `on`, `off` or a switch parameter: `boost;` and `boost 1;` are refused. `cpus` also takes
  `always`.
- A range word takes `least..most`, or one value for both; either end may be a parameter.
- `finish_limit` counts seconds and `mine_regen` milliseconds, written plain: a time with a unit is steps.

### Courses and laps

| Written | What it does | Range |
|---|---|---|
| `laps n;` `laps unlimited;` | The race's laps. `unlimited` is a race with no finish line: nobody finishes by laps. Left out: 3 | 0 to 99; `unlimited` is 0 |
| `laps by course { "Mute City: Sonic Oval": 8; else: 3; }` | Laps by the course the race is on; `else` is every course the table leaves out. A row is a number, never a parameter. Not beside `laps` unless one of them has a parameter with a named value. Beside `courses`, a row must name one of its courses | 0 to 99 a row; a cup course a row (the story stages take `else`); `else` 3 if left out |
| `course "Big Blue: Drift Highway";` | Pins one course: no vote, no course choice in the lobby. Not with `courses`. Quick Play never draws a mode with it | a course of the pool |
| `courses all;` | Every course of the pool, as leaving it out | |
| `courses ["Mute City: Sonic Oval", "Story: High-Stakes Jumps"];` | The courses the lobby's vote, random draw or fixed course may use. In Quick Play each card draws one of them | courses of the pool |

### Machines and racers

| Written | What it does | Range |
|---|---|---|
| `racers least..most;` `racers n;` | The lobby has `most` seats; a race with fewer racers than `least` is refused. Left out: the lobby's seats | 1 to 30 |
| `teams least..most;` | The race has teams; the lobby offers `most`, and a race with players on fewer teams than `least` is refused. The host picks the count only when it is a parameter. Left out: no teams | 2 to 30 |
| `cpus on;` `cpus off;` `cpus always;` | `on`: players may vote CPUs into the race; it does not force them in. `always` (api 0.6.1): CPUs take the empty seats of every race, with no vote. Left out: no CPUs. A parameter that gives it is a switch (off, on) or a choice of three, read in that order as off, on, always: `param cpu_racers "CPU racers" choice ["Off", "Vote", "Always"] default "Vote";` | on, off or always |
| `cpu_level n;` | The CPUs' level: 0 Novice, 1 Standard, 2 Expert, 3 Master. Left out: Master | 0 to 3 |
| `machines all;` | Every machine, as leaving it out | |
| `machines ["Blue Falcon", "Golden Fox"];` | The machines racers may choose and CPUs drive | machine names |
| `customs on;` `customs off;` | `off`: no custom machines; a racer who chose one races their stock machine. Left out: allowed | on or off |
| `machine "Blue Falcon";` | Every racer, CPUs too, races this machine. A machine a mode sets is the stock one (a racer's custom machine is not raced), and the machine choice offers it alone; the server refuses another. Not with another `machine` form | a machine name |
| `machine random;` | One machine drawn for the race from the machines `machines` allows (left out: all 41); every racer, CPUs too, races it | |
| `machine random each;` | Every racer, CPUs too, races a machine of their own drawn from the machines `machines` allows | |
| `machine by seat ["Blue Falcon", own, random];` | Seat 0 races the list's first entry, seat 1 the next, and so on. `own` is the racer's own choice (a custom machine too, unless `customs off`; a CPU's usual machine), `random` a draw from what `machines` allows. A seat past the list's end is `own` | at most 30 entries |
| `machine by team ["Blue Falcon", "Fire Stingray"];` | Team 1 races the first entry, Team 2 the next, and so on; entries as `machine by seat`. Needs `teams` | at most `teams`' most entries |
| `machine by place ["Black Bull", random, own];` | By the lobby's last race: its winner races the first entry, second place the next, and so on; entries as `machine by seat`. A racer it did not rank (the lobby's first race, a racer who joined since, a CPU) is `own` | at most 30 entries |
| `machine_setting least..most;` `machine_setting n;` | Each racer's machine setting is brought into this range. Left out: each racer's own | 0 to 100 |
| `stats "GX Unleashed";` | The race's stat pack, by its name; a server without it refuses the mode. With a choice parameter, each choice is a pack's name, `stock`, or `file` (the host's own `.fzstats` file, which the launcher then asks for) | a pack name, at most 64 characters |
| `stats stock;` | The disc's own stats, as leaving it out | |
| `mixed_packs on;` | Each racer may race a pack of their own. Left out: one pack for every racer | on or off |
| `max_energy n;` | Every machine's full energy; every machine begins full. Left out: 100 | 1 to 1000 |

### Start

| Written | What it does | Range |
|---|---|---|
| `rolling_start 1000 km/h;` `rolling_start;` | Every machine moves at this speed from the first picture, with full energy, a CPU steering it through the countdown at 60% accelerator with no boost or attacks; every pad takes over at GO | 0 to 9000 km/h (above with `seat_speed_open;`); 1000 if left out |
| `seat_speed_open;` | `seat.speed` and `rolling_start` may go past 9000 km/h. A 0.6.0 game cannot race a mode with it | |
| `max_speed_open;` | `seat.stat.max_speed` may go past a stat pack's 4, to 10,000. A 0.6.0 game cannot race a mode with it | |
| `start_gap 200 m;` | Machines start this many metres apart, going back from the line in grid order across three lanes; at most the lap over the racers, so the field fits one lap. Every machine starts behind the line, and its first crossing counts no lap. `0` puts everyone on one spot with contact off from the first step, and refuses `contact on`. Above 0 and below 12.9 m (the game's grid step) needs `contact off` | 0 to 10,000 m |
| `start_gap even;` | The machines spread evenly round the whole lap behind the line. Not with `start_gap` in metres | |
| `boost_start on;` | Boosting is allowed on the first lap. Left out: not | on or off |

### Race rules

| Written | What it does | Range |
|---|---|---|
| `pedals locked;` `pedals locked accel 0.8 brake 0.0;` | Every machine's accelerator and brake are held, CPUs too; steering, strafe, spin and side attacks stay the driver's. An accelerator below 0.5 cancels side attacks; 0, or any brake, refuses every boost | 0 to 1 each; accel 1.0 and brake 0.0 if left out |
| `boost off;` `boost on;` | `off`: no machine can start an energy boost; a dash plate still boosts | on or off |
| `contact off;` `contact on;` | `off`: every machine passes through every other | on or off |
| `restore on;` | Every machine is restored after a wreck (a fall, an explosion, running out of energy; never time over) instead of going out. Left out: no restores | on or off |
| `wreck_slide off;` | A machine that gets restores and breaks down is restored at once, as before 0.6.1. Left out or `on`: it slides on as a wreck, can cross the line and finish, and its restore starts when a race without restores would have taken it out. A 0.6.0 game cannot race a mode with it | on or off |
| `mine_regen 200;` `mine_regen on;` `mine_regen off;` | A mine comes back this many milliseconds after it goes off; `on` is 200. Left out or `off`: it stays gone | 50 to 5000 ms |

### Combat

| Written | What it does | Range |
|---|---|---|
| `damage 200%;` | Machine contact's damage; the cap of 20 a hit for a player moves with it. Walls, damage strips and mines are not machine contact | 10% to 1000% (0.1 to 10.0) |
| `knockback 150%;` | Machine contact's push, the attacks' push with it | 10% to 1000% |
| `wall_damage 50%;` | A wall's damage | 10% to 1000% |
| `wall_bounce 200%;` | A wall's bounce. The game's bounce acts only above about 400 km/h | 10% to 1000% |
| `spin_attacks off;` | No machine spin-attacks, CPUs included | on or off |
| `side_attacks off;` | No machine side-attacks | on or off |
| `spin_immunity on;` | A spinning machine takes no contact damage | on or off |
| `damage_cap off;` | A player's hit is no longer held to 20 (20 × `damage` for contact), as a CPU's never is. A hit still takes at most 1.01 × full energy; walls and mines keep their own caps | on or off |

### End and results

| Written | What it does | Range |
|---|---|---|
| `finish_limit 90;` | The race stops waiting for the machines still racing this many seconds after the first finish (0: never). The results follow once the last player to finish has had FINISH! on screen, 8 seconds at most; a machine that crosses the line in that wait still finishes. Left out: the race waits for every player | 0 to 600 seconds |
| `clock_limit 5min;` | The game's timed race: at this time from GO the race ends, with places by the race order unless the mode gave them. The clock shows only with `show s, TIMER`. Not with `show_clock` | 1 s to 18 min 12 s (60 to 65,535 steps) |
| `time_bonus 5;` `time_bonus on;` `time_bonus off;` | With `clock_limit`: the first machine past each new quarter of the race's laps adds this many seconds to the clock, and TIME BONUS shows; `on` is 5 | 1 to 120 seconds |
| `race_waits everyone;` | The race goes on after the last player is out, until the mode ends it or every machine is finished or out. Needs `end_race` in a rule, or `clock_limit` | |
| `points_table game;` `points_table [10, 6, 4, 3, 2, 1];` | What each place earns when the mode ends the race, added to `award`'s: `game` is the Grand Prix's (100, 93, 87 … 15); a list gives places from 1. Numbers only. Ends on the points page. Needs `end_race` or `clock_limit` | at most 30 places, 0 to 999 each |
| `results points;` | The times page passes by itself, and the results are the points page. Needs `award` or `points_table` | |
| `results_title "KNOCKOUT";` | The points page's title in place of VS TOTAL RANKING. A mode with a title ends on the points page, every row 0 without `award` | A to Z and the space, 1 to 23 characters, no space at either end |
| `results_heading "OVERDRIVE";` | Heads the times list of a mode with no points page. Refused beside `award`, `results_title` or `points_table`. Left out: RESULTS | as `results_title` |
| `results_times off;` | The results show no page of race times and open on the points page, for a mode that is not scored by time. Needs a points page (`award`, `points_table` or `results_title`). A 0.6.0 game cannot race a mode with it | on or off |
| `live_places;` | The places the mode gives show during the race: the place at the top of the screen and the faces at the side follow `seat.place`, from the step it is written. Seats with no place follow in race order. Left out: `seat.place` counts only when the race ends. A 0.6.0 game cannot race a mode with it | |
| `hud clean;` | Nothing over the race but the HUD's own parts: no CHECK sign and no name tags, a player's or a `tag`. For footage | |
| `distance_label off;` | DISTANCE shows its number and m without the game's REST label, for metres that are not the metres left; the number then has six digits (to 999999) in place of five. A 0.6.0 game cannot race a mode with it | on or off |
| `hud_layout ax;` | The race HUD in F-Zero AX's layout: the position at the bottom left, the faces board smaller to leave it room, TIMER at the top. The rest stays where GX puts it; the spare-machine counter (`SPARE`) keeps its place at the bottom left under the position, so a mode that shows both places one of them with `hud_move` | |

A race ends by laps and `finish_limit`, at `clock_limit`, at `end_race`, or when the last player is finished or out
(unless `race_waits everyone`). The results are the times list, then the points page when the mode awards points or
has a title or a points table.

### Raced alone

| Written | What it does | Range |
|---|---|---|
| `solo on;` | Raced alone: one seat, no relay, started by the launcher, as Play's Practice is. No lobby races it. Not with `cpus`, `racers` or `teams` | on or off |
| `rewind 5min;` | How far back the player can go. Needs `solo on` | 1 s to 5 min |
| `pause on;` | Start stands the picture; the stick and the bar move it within the rewind. Needs `solo on` | on or off |
| `save_state on;` | One saved moment: D-pad right saves, left loads. Needs `rewind` | on or off |
| `resume_countdown 1s;` | The wait after a resume or a load. Needs `solo on` | 0 to 5 s |

## Events

| Event | When | Binds |
|---|---|---|
| `game_start` | Once, before the first picture with the machines in it | nothing |
| `countdown` | The first step the machines run: the 3-2-1 begins (with `rolling_start`, at once) | nothing |
| `race_start` | GO | nothing |
| `every step` | Every step, 60 a second | nothing |
| `each seat` | Every step, for each seat in seat order | `seat` |
| `lap(s)` | s completed a lap | a seat |
| `checkpoint(s)` | s reached another checkpoint | a seat |
| `finish(s)` | s finished | a seat |
| `overtake(a, b)` | a, racing, passed b, racing, in the race order | two seats |
| `wrong_way(s)` | s began going the wrong way | a seat |
| `out(s, cause)` | The game booked s out. `cause`, the first that holds: 5 time over; 2 exploded; 3 fell off the course; 1 out of energy, at rest; 4 retired; 0 none of these | a seat, an int |
| `fall(s)` | s left the course with no explosion | a seat |
| `destroyed(s)` | s exploded, once for each life. It may come after s's `out`: a wreck at rest explodes when another machine touches it | a seat |
| `ko(v, by)` | v's contact KO began. `by` is the machine the game credited, or `none`: a wall, a strip or a mine credits nobody, and neither does a step with two KOs | two seats |
| `respawned(s)` | s's restore is over: it drives again | a seat |
| `hit(a, b)` | a and b began touching; a has the lower seat number | two seats |
| `damaged(s)` | s's energy fell while it was not boosting | a seat |
| `wall(s)` | s began touching a wall | a seat |
| `pit(s)` | s came onto a pit strip | a seat |
| `boost(s)` | A boost began: an energy boost or a dash plate | a seat |
| `boost_end(s)` | A boost ended | a seat |
| `land(s)` | s came down from the air | a seat |

## Seat reads

Written on a seat: `seat.speed`, `s.energy`, `first(seats).lap`. The value as it stood when the step began.

### State

| Read | What it gives | Type |
|---|---|---|
| `seat.racing` | Neither finished nor out | bool |
| `seat.finished` | It has finished | bool |
| `seat.out` | The game has booked it out | bool |
| `seat.broken` | Broken down: out of energy | bool |
| `seat.destroyed` | This life ended in an explosion | bool |
| `seat.fallen` | Destroyed or off the course | bool |
| `seat.restoring` | The game is restoring it after a wreck | bool |
| `seat.index` | The seat's number | int, 0 to 29 |
| `seat.team` | Its team; without teams, its seat number. The lobby sets it | int, 0 to 29 |
| `seat.machine` | Its machine; 50 a custom machine | int, 0 to 40 or 50 |
| `seat.is_human` | A person drives it | bool |
| `seat.is_cpu` | No person drives it | bool |

### Place on the course

| Read | What it gives | Type |
|---|---|---|
| `seat.standing` | Its place in the race order now, 1 first: the game's own order, also when the mode shows other places with `live_places;` | int |
| `seat.lap` | Laps completed | int |
| `seat.distance` | Metres along the course over all laps | real |
| `seat.lap_distance` | Metres into this lap | real |
| `seat.lap_length` | The length of one lap, in metres | real |
| `seat.checkpoint` | The course checkpoint it is at | int |
| `seat.checkpoint_fraction` | How far it is toward the next checkpoint | real, 0 to 1 |
| `seat.wrong_way` | The game says it is going the wrong way | bool |
| `seat.position` | Where it is | vec |
| `seat.surface` | What it is on: 1 pit strip, 2 dash plate, 3 jump plate, 4 ice, 5 dirt, 6 damage strip, 7 mine, 0 none of these | int |
| `seat.in_pit` | On a pit strip this step | bool |
| `seat.used_pit` | Has used a pit this race | bool |

### Motion

| Read | What it gives | Type |
|---|---|---|
| `seat.speed` | Its speed | real, km/h |
| `seat.top_speed` | Its highest speed this race, as the results show it | real, km/h |
| `seat.velocity` | Its motion | vec, km/h |
| `seat.forward` | Its nose, length 1 | vec |
| `seat.up` | Its up, length 1 | vec |
| `seat.airborne` | All four hover points are in the air | bool |
| `seat.boosting` | A boost is running: an energy boost or a dash plate | bool |
| `seat.boost_left` | Steps left on the running boost: while `boosting`, `boost_left + 1` remain, this one counted | int |
| `seat.base_speed` | The speed its engine runs at, in the game's units (a metre a step): the speed shown follows it through the game's drive, grip and air drag. Cruising it is about f + f × f / 125, with f the speed shown / 216: 4.8 at 1000 km/h, 27 at 5000 | real |
| `seat.boost_value` | The game's boost value: each boost adds to it and it falls away after; the thrust reaches higher with it | real |

### Energy and contact

| Read | What it gives | Type |
|---|---|---|
| `seat.energy` | Its energy | real |
| `seat.max_energy` | Its full energy: 100 unless the mode changed it | real |
| `seat.last_hit_energy` | The energy its last hit took | real |
| `seat.damage_taken` | Energy lost to damage this race | real |
| `seat.hits` | Contact hits this race | int |
| `seat.touching` | The seats in contact with it this step | seats |
| `seat.no_contact` | Nobody touches it | bool |
| `seat.spinning` | It is spin-attacking | bool |
| `seat.side_attack` | It is side-attacking | bool |
| `seat.kos` | Its KO count: the skulls the game credited it | int |

### Times and results

| Read | What it gives | Type |
|---|---|---|
| `seat.race_time` | Steps from GO to its finish, or to now while it races | int |
| `seat.mode_lap_steps` | Steps since its last lap, or GO | int |
| `seat.game_lap_steps` | The game's own clock of this lap | int |
| `seat.best_lap` | Its fewest steps for a lap this race; 0 before its first | int |
| `seat.gap_ahead` | Steps behind the racer one place ahead, timed where both passed (64 points a lap); -1 with nobody there | int |
| `seat.gap_behind` | Steps ahead of the racer one place behind; -1 with nobody there | int |
| `seat.gap_leader` | Steps behind the leader; 0 for the leader, -1 before anyone has a time. A racer a lap or more away reads as the time since it last passed here | int |
| `seat.place` | The place the mode gave it with `seat.place = n`; 0 none | int |
| `seat.points` | The points the mode gave it with `award` | int |

### Controller

| Read | What it gives | Type |
|---|---|---|
| `seat.input.accel` | The accelerator it drove with this step (a CPU's may pass 1) | real, 0 to 1 |
| `seat.input.brake` | The brake it drove with this step | real, 0 to 1 |
| `seat.input.steer` | Its steering this step | real, -1 to 1 |
| `seat.input.strafe` | Its strafe this step | real |
| `seat.input.pitch` | Its pitch this step | real |
| `seat.pad.steer` | The stick as the player holds it this step, read while the race is paused too (`seat.input.*` stand still then); 0 for a CPU | real, -1 left to 1 right |
| `seat.pad.accel` | The accelerator (A) as the player holds it this step, paused or not; 0 for a CPU | real, 0 to 1 |
| `seat.pad.brake` | The brake as the player holds it this step, paused or not; 0 for a CPU | real, 0 to 1 |
| `seat.pad.boost` | The boost button is held this step, paused or not; false for a CPU | bool |

## Machine stats

`seat.stat.NAME` is the stat the machine drives with this step; `seat.stat.base.NAME` its own, before the mode changed
it; `seat.stat.NAME = v;` (and `+=`, `-=`, `*=`, `/=`) changes it from the next step, held to the range a stat pack may
use (`seat.stat.max_speed` to 10,000 with `max_speed_open;`: at that the game no longer pulls the engine's speed down), until written again
or the race ends, a restore included. `seat.stat.NAME = seat.stat.base.NAME;` gives the
machine its own back. The base cannot be written. All are reals.

| Written | What it is | Takes effect |
|---|---|---|
| `seat.stat.weight` `seat.stat.weight = v` `seat.stat.base.weight` | The machine's weight | every step |
| `seat.stat.accel` `seat.stat.accel = v` `seat.stat.base.accel` | Acceleration | every step |
| `seat.stat.max_speed` `seat.stat.max_speed = v` `seat.stat.base.max_speed` | Top speed | every step |
| `seat.stat.grip` `seat.stat.grip = v` `seat.stat.base.grip` | Grip in the corners | every step |
| `seat.stat.turn` `seat.stat.turn = v` `seat.stat.base.turn` | Turning | every step |
| `seat.stat.strafe` `seat.stat.strafe = v` `seat.stat.base.strafe` | Strafing | every step |
| `seat.stat.drag` `seat.stat.drag = v` `seat.stat.base.drag` | Drag | every step |
| `seat.stat.boost_strength` `seat.stat.boost_strength = v` `seat.stat.base.boost_strength` | A boost's strength (a stat pack's value × 0.57) | the next boost |
| `seat.stat.boost_length` `seat.stat.boost_length = v` `seat.stat.base.boost_length` | A boost's length, in seconds | the next boost |
| `seat.stat.boost_energy` `seat.stat.boost_energy = v` `seat.stat.base.boost_energy` | The energy a boost takes each step (the game's 1/6; a stat pack may set its own) | every step |
| `seat.stat.body` `seat.stat.body = v` `seat.stat.base.body` | The damage a hit does to it, as a multiplier | the next hit |
| `seat.stat.drift_rotation_resistance` `seat.stat.drift_rotation_resistance = v` `seat.stat.base.drift_rotation_resistance` | How much a sliding corner turns the machine | every step |
| `seat.stat.drift_friction` `seat.stat.drift_friction = v` `seat.stat.base.drift_friction` | A sliding corner's grip | every step |
| `seat.stat.turn_tension` `seat.stat.turn_tension = v` `seat.stat.base.turn_tension` | Resistance to turning | every step |
| `seat.stat.turn_reaction` `seat.stat.turn_reaction = v` `seat.stat.base.turn_reaction` | How far it leans into the steering | every step |
| `seat.stat.strafe_turn` `seat.stat.strafe_turn = v` `seat.stat.base.strafe_turn` | The turn a strafe adds | every step |
| `seat.stat.drift_accel` `seat.stat.drift_accel = v` `seat.stat.base.drift_accel` | Drift acceleration | every step; a CPU keeps the one it began the race with |
| `seat.stat.turn_decel` `seat.stat.turn_decel = v` `seat.stat.base.turn_decel` | The speed lost to sliding sideways | every step |
| `seat.stat.grip_window` `seat.stat.grip_window = v` `seat.stat.base.grip_window` | Steps of full grip after the accelerator is newly full on the ground, 0 to 255 | the next fresh press |

## Race reads

| Read | What it gives | Type |
|---|---|---|
| `race.steps` | Steps since GO | int, 60 a second |
| `race.started` | GO has happened | bool |
| `race.seats` | The race's machines | int |
| `race.laps` | The race's laps; 0 unlimited | int |
| `race.course` | The course's number | int |
| `race.humans_left` | Seats a person drives that are neither finished nor out | int |
| `race.human_finished` | Seats a person drives that have finished | int |
| `race.damage` | Machine contact's damage now; 1.0 is the game's own | real |
| `race.knockback` | Machine contact's push now | real |
| `race.wall_damage` | A wall's damage now | real |
| `race.wall_bounce` | A wall's bounce now | real |
| `race.spin_attacks` | Spin attacks are on | bool |
| `race.side_attacks` | Side attacks are on | bool |
| `race.spin_immunity` | Spin immunity is on | bool |
| `race.paused` | The race stands: `pause` held it and no `resume` has let it go yet (a resume's countdown counts as paused) | bool |
| `race.paused_steps` | Steps the race has stood in this pause; 0 while it runs | int, 60 a second |

## Actions

An action is a statement, its seat first; the commas between its values are optional. A word written
`seat.NAME = v;` also takes `+=`, `-=`, `*=` and `/=`. Actions take effect from the next step.

### Speed and motion

| Written | What it does | Range |
|---|---|---|
| `seat.speed = v;` | Holds the seat's speed for the next step's travel; write it every step to keep it. A seat frozen, broken down, destroyed, finished, retired or restoring is not held. Long falls get steeper and jumps lower at a held speed; boosting still costs energy. Above the stat pack's speed cap (stock 9990 km/h; GX Unleashed has none) the game cuts the speed to a near stop once the hold ends | 0 to 9000 km/h; above with `seat_speed_open;` |
| `seat.base_speed = v;` | Sets the speed the seat's engine runs at (see `seat.base_speed`); the next step's thrust, drag and walls move on from it, and the machine's grip, slides and slopes act as ever. Nothing is held. A seat frozen, broken down, destroyed, finished, retired or restoring is left as it is | 0 and up, the game's units |
| `seat.velocity = v;` | Sets the seat's motion for one step's travel; from the next step the game's forces act again | vec, km/h |
| `push s, v;` | Adds v to the seat's motion in the next step | vec, km/h |
| `freeze s;` | The seat stands still until released; it can still turn | |
| `release s;` | A frozen seat drives again | |

### Energy and boost

| Written | What it does | Range |
|---|---|---|
| `seat.energy = v;` | Sets the seat's energy | 0 to 101% of its full energy |
| `seat.max_energy = v;` | The seat's full energy; a fraction is dropped. The energy stays where it is unless above the new full. To fill too: `s.max_energy = 150; s.energy = 150;` | 1 to 1000 |
| `damage s, x;` | The game's own damage: x times the machine's `body`, at most 20 for a player (unless `damage_cap off`) and 1.01 × full energy. Nothing on a finished or restoring seat. Below 0 energy the machine breaks down. Credits no KO; several in a step add up | x from 0 |
| `boost s;` | Asks for the game's own energy boost, with the game's rules: refused on the first lap (without `boost_start`), with too little energy, with the brake on or the accelerator at 0, or with a boost running. A refused request lasts that step only | |
| `boost s off;` | The seat can no longer boost, to the race's end. A running boost and a dash plate are not stopped | |

### Ending a seat or the race

| Written | What it does | Range |
|---|---|---|
| `retire s;` | The seat retires: the game books it out at the next step. Not on a finished seat | |
| `explode s;` | The seat explodes and is out at the next step, or is restored if it respawns. Credits no KO. Nothing on a finished, restoring or already destroyed seat | |
| `break_down s;` | The seat runs out of energy as after a last hit: it tumbles, and is out by the game's verdict when it comes to rest, explodes or falls. `ko` fires for it | |
| `danger_zone s, below, arm, seconds;` | The story's speed bomb: armed once the seat reaches `arm` km/h; below `below` km/h a count of `seconds` runs down, and at its end the seat explodes. Show the count with `show s, DIGIT` | seconds 1 to 9 |
| `danger_zone s off;` | The seat carries no bomb; a count under way stops | |
| `finish_seat s;` | The seat finishes here and now, as if it had crossed the line: FINISH!, its place, and the CPU drives on. A place the mode gave it stays. Not on a finished or out seat | |
| `end_race;` | The race ends for everyone at this step, after every other action of the step | |
| `stop_all;` | Every machine brakes. It ends nothing: a mode with it needs `end_race` or `clock_limit` | |

### Restores

| Written | What it does | Range |
|---|---|---|
| `respawns s;` | The game restores the seat after a wreck instead of booking it out: a fall, an explosion, running out of energy; never time over. To restore a healthy machine: `respawns s;` then `explode s;` | |
| `respawns s off;` | The seat's next wreck is final; a restore under way finishes | |

### Contact and combat

| Written | What it does | Range |
|---|---|---|
| `no_contact s;` | Nobody touches the seat, and it touches nobody | |
| `no_contact s off;` | The seat is solid again. Bring contact back only when the seats are apart: a pair that still overlaps may be pushed apart hard | |
| `no contact a, b;` | Every seat of a passes through every seat of b | each a seat or a set |
| `team t no contact;` | Teammates of team t pass through each other and still bump everyone else | team 0 to 29 |
| `race.damage = v;` | Machine contact's damage from the next step | 10% to 1000% |
| `race.knockback = v;` | Machine contact's push from the next step | 10% to 1000% |
| `race.wall_damage = v;` | A wall's damage from the next step | 10% to 1000% |
| `race.wall_bounce = v;` | A wall's bounce from the next step | 10% to 1000% |
| `race.spin_attacks = b;` | Spin attacks on or off from the next step | bool |
| `race.side_attacks = b;` | Side attacks on or off from the next step | bool |
| `race.spin_immunity = b;` | Spin immunity on or off from the next step | bool |

### CPUs and pads

| Written | What it does | Range |
|---|---|---|
| `hand_to_cpu s;` | The CPU drives the seat from the next step | |
| `pads_back;` | Every seat handed to the CPU drives on its pad again | |
| `cpu_mode s, n;` | A CPU seat drives one of the story mode's scripted drivers; 0 is the normal driver. Nothing on a player's seat | 0 to 33 |

### Results

| Written | What it does | Range |
|---|---|---|
| `seat.place = n;` | The seat's place in the results when the race ends, 1 first (0: none). When the mode ends the race, the seats it placed keep their places, every other racing seat follows in race order, and a placed seat that went out shows the time it went out. With `live_places;` the place shows during the race too | 1 first; 0 none |
| `award s, n;` | Adds n points to the seat. The results show a negative total as 0 | any whole number |

### Pause

A paused race stands: no machine, CPU, clock or timer moves and the picture stays, while the mode's rules go on running
every step, reading the pads (`seat.pad.*`) and counting (`wait`, `race.paused_steps`). Words that act in the race's
step (`seat.speed`, `boost`, `push`, `damage`, `explode`) do nothing while it stands; a seat's field (`seat.energy`)
changes at once. In 0.6.1 a mode that pauses is raced by one player (CPUs allowed): a match with two players is refused.

| Written | What it does | Range |
|---|---|---|
| `pause;` | The race stands from the next step on | |
| `resume;` | The race runs on from the next step | |
| `resume after 1s;` | The race stands that long more, with a 3, 2, 1 over the picture, then runs on | steps; 0 at once |

## Display words

Displays are the game's own HUD parts, banners and looks, and Piconet's name tags and course map; a mode cannot draw
text where it likes. They never change the race.

### HUD

| Written | What it does | Range |
|---|---|---|
| `show s, PART;` | The seat's player sees a part of the race HUD. The standard parts are on until hidden; the others are off until shown | a part |
| `hide s, PART;` | The seat's player no longer sees the part | a part |
| `show_number s, n;` | A number in two digits (07) beside the spare-machine icon; above 99 shows 99, a negative number hides it. A spectator sees the watched racer's | whole number |
| `show_number s off;` | Hides the number | |
| `show_score s, n;` `show_score s, n, "KOS";` | A counter where the race time is (top right, under the energy meter): n in the race time's own digits under a short label, SCORE unless the text says otherwise. A spectator sees the watched racer's; `hud_move s, SCORE, ...` moves it | 0 to 999,999 (a negative number reads 0); the label as `tag`'s text: A to Z, 0 to 9, space, `.` and `-`, 1 to 10 characters, at most 8 texts a mode with `tag`'s |
| `show_score s off;` | The counter goes | |
| `stock_banner s, BANNER;` | The seat's player sees one of the game's banners once, as the game raises it | a banner |
| `show_clock n;` | The timed race's clock reads n steps, shown as seconds rounded up, red below 10. It ends nothing and must be written every step it should follow; players see it with `show s, TIMER`. Not with `clock_limit` | steps |
| `show_distance s, metres;` | The DISTANCE part reads these metres instead of the metres to the finish; under 1000 drawn large in the middle | 0 to 99,999; 0 to 999,999 under `distance_label off;` |
| `show_distance s off;` | DISTANCE reads the metres to the finish again | |
| `show_objects s, n, of;` | The OBJECTS part reads n/of | 0 to 99 each |
| `show_objects s off;` | OBJECTS reads the course's own count again | |
| `hud_move s, PART, x, y, angle;` | The seat's player sees the part with its centre at (x, y) of the 640 x 480 screen, turned angle degrees clockwise: the same point of the screen in 4:3, Wide Screen and true wide screen. The parts' own centres: RANK (320.5, 62), or (86, 435) under `hud_layout ax`; LAPS (439, 43); SPEED (550, 440); ENERGY (548.5, 43); LAP_TIMES (556.8, 126.6); RACE_TIME (532, 86); FACES (51, 214); SPARE (56, 444); SKULLS (608, 120), its first skull; BANNERS (320, 150); WRONG_WAY and FINISH (320, 240); CHECK (320, 445); SCORE (552, 86). LEADER_GAP moves the banners with it | x and y -1000 to 1000, the angle 0 to 359 |
| `hud_move s, PART off;` | The part is drawn where the game draws it again | |
| `say "Hold A to accelerate.";` | A message for every player on a dark panel: in the middle of the picture while the race is paused, as a line at its foot while it runs, until the next `say` or `say off`. Written in quotes, never a variable | any text, up to 160 bytes of UTF-8; at most 32 texts a mode |
| `say off;` | The message goes | |

### Machine and map

| Written | What it does | Range |
|---|---|---|
| `seat.look = LOOK;` | How everyone sees the seat's machine | a look |
| `seat.own_look = LOOK;` | How the seat's own driver sees it; left unset, `seat.look` | a look |
| `seat.colour = 0xRRGGBBAA;` | The machine's body colour; 0 gives it its own back | colour |
| `seat.ghost_tint = 0xRRGGBBAA;` | A ghost's colour; 0, the default, is Time Attack's yellow. The alpha is not read | colour |
| `seat.ghost_sound = on;` | A ghost keeps its engine sound; off by default, as in Time Attack | bool |
| `map_hide s from SET;` | s's dot is left off the course maps of the seats in SET; an empty set shows it to everyone | seats |
| `map_mark s, 0xRRGGBB;` | s's dot's colour on every course map; -1 gives it its machine's colour | colour |
| `map_char s, "K";` `map_char s, CROWN;` | The seat's dot on every course map carries this character, or the shape CROWN, as large as the big dots, in the dot's colour; a character the game's fonts lack shows the plain dot | one character, or CROWN |
| `map_char s off;` | The dot shows no character again | |
| `tag s, "TEXT";` | TEXT floats over the seat's machine in place of its name, for everyone else, CPUs too. Written in quotes, never a variable | A to Z, 0 to 9, space, `.` and `-`; 1 to 10 characters, no space at either end; at most 8 texts a mode |
| `tag s off;` | The tag goes; the seat's own name tag, if it has one, comes back | |

## Display values

### Parts

| Part | What it is | Shown |
|---|---|---|
| `RANK` | The place in the race | until hidden |
| `LAPS` | The lap count | until hidden |
| `SPEED` | The speedometer | until hidden |
| `ENERGY` | The energy meter | until hidden |
| `LAP_TIMES` | The lap times | until hidden |
| `RACE_TIME` | The race time | until hidden |
| `FACES` | The racers' faces | until hidden |
| `SPARE` | The Grand Prix spare-machine counter: the star icon and `show_number`'s number. For `hud_move` only: `show_number` shows and hides it (0.6.1) | with `show_number` |
| `SKULLS` | The KO skulls at the right edge. For `hud_move` only (0.6.1) | as the game shows them |
| `WRONG_WAY` | The WRONG WAY sign (the banner of the same name). For `hud_move` only (0.6.1) | as the game shows it |
| `BANNERS` | FINAL LAP, BOOST POWER, TIME BONUS and the gap popup together; a `LEADER_GAP` place takes them too, a `BANNERS` place of its own wins. For `hud_move` only (0.6.1) | as the game shows them |
| `FINISH` | FINISH and RETIRED. For `hud_move` only (0.6.1) | as the game shows them |
| `CHECK` | The CHECK sign at the foot of the screen that points at the rival close behind: `hud_move` moves its row, and it still slides sideways after that rival. For `hud_move` only (0.6.1); `hud clean` takes it off | as the game shows it |
| `SCORE` | A mode's counter at the race time's place: `show_score`'s number in the race time's digits under its label. For `hud_move` only: `show_score` shows and hides it (0.6.1) | with `show_score` |
| `TIMER` | The timed race's clock, with `clock_limit` or `show_clock` | when shown |
| `LEADER_GAP` | The gap to the leader at each lap line, for a second; the leader sees its lead when second place crosses | when shown |
| `GHOST_GAP` | Time Attack's live gap at the right edge, all race, against the seat furthest ahead; red while it is ahead | when shown |
| `DISTANCE` | REST and the metres to the finish, or `show_distance`'s; without REST under `distance_label off;` | when shown |
| `OBJECTS` | The objects count, or `show_objects`'s | when shown |
| `DIGIT` | The story mode's big 3-2-1 digit; it shows a `danger_zone`'s count | when shown |
| `WARNING` | The story mode's warning sign | when shown |
| `SPEED_GAUGE` | The story mode's speed gauge | when shown |
| `SUCCESS` | The story mode's MISSION CLEAR! | when shown |
| `FAILED` | The story mode's MISSION FAILED | when shown |
| `TIME_OVER` | The story mode's "Time is up..." | when shown |

### Banners

| Banner | What it is |
|---|---|
| `FINAL_LAP` | FINAL LAP |
| `BOOST_POWER` | BOOST POWER |
| `TIME_BONUS` | TIME BONUS |
| `GAP` | The gap popup for the seat's last lap line |
| `WRONG_WAY` | WRONG WAY |
| `TICK` | The timed race's countdown beep |

### Looks

| Look | What it is |
|---|---|
| `normal` | The game's own |
| `ghost` | Drawn as Time Attack draws its ghosts: see-through, no shadow, fading in over a second. It keeps its dot on the course map unless the mode hides it |
| `invisible` | Not drawn at all |

## Library

Written in the mode language and part of every file. A macro is used at the top level; a sub inside an expression.

| Written | What it does | Takes |
|---|---|---|
| `every(t) { … }` | The block runs at every multiple of t after GO: 30 s, 60 s, 90 s… | a time, or a `delay` parameter without a named value; at least one step |
| `at(t) { … }` | The block runs once, when the race clock reaches t | a time |
| `lives n;` | Every wreck the game restores costs a life: a hit, running out of energy, a fall, a breakdown. While a seat has more than one life the game restores it; the wreck that leaves it one turns its restores off, so its next wreck is final. The lives remaining after the current one show beside the spare-machine icon (2, 1, 0 with `lives 3`; 0 with `lives 1`), hidden once out. Declares the seat variable `lives_left`, which counts the current life too (3, 2, 1; 0 once out). `lives 1` is the game's own rule | a whole number or parameter |
| `pits_off;` | The pit strips give no energy. A seat in a pit keeps the energy it came in with (less if it is hit there). Declares `pits_off_energy` | |
| `drive target, brake_cost, surge, plates, gate;` | Twilight's drive (their survival mod) for every racing seat while `gate` holds (it may name `seat`; `true` for always; `plates` true or false), on the engine's speed so grip, slides, slopes and walls still move the machine: the top speed stat goes to 10,000 and drag to 0; the engine's speed is pulled towards the target (km/h), lowered by the brake (full brake: 0), firmer with the accelerator; braking costs energy (`brake_cost` % of Twilight's), more as the target rises and the longer it is held, never below 0.1; a boost raises the wanted speed by `surge` % plus 1 % a point of `seat.boost_value`, by how far the accelerator is down, a dash plate's too when `plates` is on. Needs `max_speed_open;`. Declares `drive_heat`, `drive_tuned`, `drive_was_boosting`, `drive_plate` | a real (km/h), two reals (%), a bool, a bool |
| `gap_to(a, b)` | The distance along the course between two seats, wrapped at the finish line, the shorter way round | two seats; gives a real, metres |
| `dist(a, b)` | The straight-line distance between two seats. It holds a `sqrt` | two seats; gives a real, metres |

Write `lives 3;`, `pits_off;` and `drive t, c, s, p, g;`: `lives(3);` and `pits_off();` are refused. A file cannot
declare `lives_left`, `pits_off_energy`, the four `drive_` words, `gap_to` or `dist` again.

## Operators and functions

| Operators, loosest first | |
|---|---|
| `or` | Either; stops at the first true |
| `and` | Both; stops at the first false |
| `not` | |
| `==` `!=` `<` `<=` `>` `>=` `in` | One comparison at a time: `a < b and b < c`. `bool`, `seat` and `seats` take `==` and `!=` only |
| `+` `-` | Numbers, vecs and sets |
| `*` `/` `%` `&` | `%` takes whole numbers only; `&` is the sets' intersection |
| `-` in front | |
| `.x`, calls | |

A whole number meets a real as a real. A real becomes a whole number only through `floor`, `ceil`, `round` or `trunc`.

| Function | Gives |
|---|---|
| `abs(x)` | The size of x, of x's type |
| `floor(x)` | A real made whole, down |
| `ceil(x)` | A real made whole, up |
| `round(x)` | A real made whole, to the nearest; a half away from zero |
| `trunc(x)` | A real made whole, toward zero |
| `sqrt(x)` | The square root, as the game works it out; 0 for 0 or less |
| `min(a, b, …)` `max(a, b, …)` | The least or most of two or more numbers |
| `random(n)` | A whole number from 0 to n − 1; 0 when n ≤ 0 |
| `random_real()` | A real from 0 up to 1, not 1 |
| `vec(x, y, z)` | A vec |
| `dot(a, b)` | The dot product of two vecs |
| `length(v)` | A vec's length |
| `cross(a, b)` | The cross product of two vecs |

`floor`, `ceil`, `round` and `trunc` refuse a whole number. A vec also takes `a + b`, `a - b`, `-v`, and a number
times or divided into it. The random numbers are the mode's own: every race draws differently, every game in a race
and its replay alike. There is no `sin`, `cos`, `pow` or `exp`.

| Set helper | Gives |
|---|---|
| `count(SET where c)` | How many seats of SET pass c |
| `first(SET where c by key)` `last(SET where c by key)` | The first or last seat in that order (seat order without `by`); `none` when none |
| `nth(SET where c by key, i)` | The seat at place i, from 0; `none` past the end |
| `sum(e for each s where c)` | The sum of e over the seats that pass |
| `min(e for each s where c)` `max(e for each s where c)` | The least or most of e; 0 when no seat passes |
| `any(e for each s where c)` `all(e for each s where c)` | Whether e holds for any seat, for all |

`where` and `by` are optional; SET may be `seats` or any set; `for each s in SET` walks only SET's seats. A helper is
a loop over the seats.

## Courses and machines

`course`, `courses` and `race.course` use the course pool, the 30 courses Piconet races. `laps by course` names the
26 cup courses; the four story stages take its `else`. `machines` names machines as below; `seat.machine` reads the
number.

| Number | Course | Number | Course |
|---|---|---|---|
| 1 | `Mute City: Twist Road` | 25 | `Sand Ocean: Lateral Shift` |
| 3 | `Mute City: Serial Gaps` | 26 | `Sand Ocean: Surface Slide` |
| 5 | `Aeropolis: Multiplex` | 27 | `Big Blue: Ordeal` |
| 7 | `Port Town: Aero Dive` | 28 | `Phantom Road: Slim-Line Slits` |
| 8 | `Lightning: Loop Cross` | 29 | `Casino Palace: Double Branches` |
| 9 | `Lightning: Half Pipe` | 31 | `Aeropolis: Screw Drive` |
| 10 | `Green Plant: Intersection` | 32 | `Outer Space: Meteor Stream` |
| 11 | `Green Plant: Mobius Ring` | 33 | `Port Town: Cylinder Wave` |
| 13 | `Port Town: Long Pipe` | 34 | `Lightning: Thunder Road` |
| 14 | `Big Blue: Drift Highway` | 35 | `Green Plant: Spiral` |
| 15 | `Fire Field: Cylinder Knot` | 36 | `Mute City: Sonic Oval` |
| 16 | `Casino Palace: Split Oval` | 39 | `Story: High-Stakes Jumps` |
| 17 | `Fire Field: Undulation` | 43 | `Story: Championship Circuit` |
| 21 | `Aeropolis: Dragon Slope` | 44 | `Story: Emperor's Trial` |
| 24 | `Cosmo Terminal: Trident` | 45 | `Story: Dimension's Edge` |

| Number | Machine | Number | Machine | Number | Machine |
|---|---|---|---|---|---|
| 0 | `Red Gazelle` | 14 | `Big Fang` | 28 | `Crazy Bear` |
| 1 | `White Cat` | 15 | `Sonic Phantom` | 29 | `Black Bull` |
| 2 | `Golden Fox` | 16 | `Green Panther` | 30 | `Dark Schneider` |
| 3 | `Iron Tiger` | 17 | `Hyper Speeder` | 31 | `Fat Shark` |
| 4 | `Fire Stingray` | 18 | `Space Angler` | 32 | `Cosmic Dolphin` |
| 5 | `Wild Goose` | 19 | `King Meteor` | 33 | `Pink Spider` |
| 6 | `Blue Falcon` | 20 | `Queen Meteor` | 34 | `Magic Seagull` |
| 7 | `Deep Claw` | 21 | `Twin Noritta` | 35 | `Silver Rat` |
| 8 | `Great Star` | 22 | `Night Thunder` | 36 | `Spark Moon` |
| 9 | `Little Wyvern` | 23 | `Wild Boar` | 37 | `Bunny Flash` |
| 10 | `Mad Wolf` | 24 | `Blood Hawk` | 38 | `Groovy Taxi` |
| 11 | `Super Piranha` | 25 | `Wonder Wasp` | 39 | `Rolling Turtle` |
| 12 | `Death Anchor` | 26 | `Mighty Typhoon` | 40 | `Rainbow Phoenix` |
| 13 | `Astro Robin` | 27 | `Mighty Hurricane` |  |  |

## Example: Knockout

Quick Play's Knockout, as it ships.

```fzmode
// A spicy Quick Play option: a rolling start, then the last racer is out at every cut.
//
// The HUD is F-Zero AX's (owner 2026-11-12): the position at the bottom left, the clock to the next cut at the top where
// AX's timer is, the six faces on AX's rows. The position and the faces follow the mode's own places during the race
// (live_places): the racers still in by the race order, then the racers out, so a cut leaves no gap in the top six.

mode "Knockout" {
  api 0.6.1;
  badge "KNOCKOUT";
  about "The racer in last place is out at every cut. A point for every cut you survive.";
}

param length    "Race length" delay 4min..10min step 30s default 7min;
param first_cut "First cut"   delay 20s..45s step 5s default 45s;    // a 30-machine grid is 374 m long
param keep      "Racers left" number 1..29 default 1;
param warn_from "Warn from"   number 3..9 default 5;                  // seconds before the cut

cpus on;
stats "GX Unleashed";   // Quick Play's pack, mines and machine setting (a word left out is the game's own)
mine_regen on;
machine_setting 100;
laps unlimited;
rolling_start 1000 km/h;       // speed at the gate, CPU steers through the countdown at 60% accelerator, control at GO
race_waits everyone;           // the race goes on until the mode ends it, not until the last player is out
results_title "KNOCKOUT";       // the points page: a point for every cut survived, ties in the order below
hud_layout ax;                 // AX's HUD: the position bottom left, the faces on AX's rows, the clock at AX's timer place
live_places;                   // the position and the faces show the places the rule "Places" gives, every step

global int interval;           // steps between cuts, worked out once at the race start
global int next_cut;
global int cut_in;             // seconds to the next cut
global int cuts;
global int hold_until;         // a player's finish: the race ends when the game's FINISH! and place have had their 8 s
seat   int cut_order;          // 0: never cut; 1: first out

rule "Arm" {
  event race_start;
  do {
    next_cut = first_cut;
    // the cuts share the race's length, never more than 30 s apart (owner 2026-11-11): a small grid ends early
    interval = min(30s, max(10s, (length - first_cut) / max(1, count(seats where racing) - keep - 1)));
  }
}

rule "Cut the last racer" {
  event every step;
  while race.started and race.steps >= next_cut and count(seats where racing and seat.cut_order == 0) > keep;
  do {
    seat v = last(seats where racing and seat.cut_order == 0 by standing);
    // every racer the same, a lone player too (owner 2026-11-05): out of energy, as a last hit: it tumbles, comes to
    // rest or explodes, and is out; its place comes with the results
    break_down v;
    cuts += 1;
    v.cut_order = cuts;
    next_cut = race.steps + interval;
    for each seat where racing and seat.cut_order == 0 { award seat, 1; }   // a point for every cut survived
  }
}

rule "Out on its own" {
  event out(s);
  do {
    // a machine, its energy or a fall: ranked by when it went out, just as a cut is; a cut seat has its order already
    if (s.cut_order == 0) { cuts += 1; s.cut_order = cuts; }
  }
}

rule "Seconds to the cut" {
  event every step;
  while race.started;
  do {
    cut_in = ceil((next_cut - race.steps) / 60.0);
    show_clock next_cut - race.steps;
  }
}

rule "Everyone sees the clock" {
  event each seat;
  when race.started;
  do { show seat, TIMER; }
}

rule "Warn the last racer" {
  event each seat;
  while race.started;
  do {
    if (seat.racing and seat.cut_order == 0 and cut_in <= warn_from
        and seat == last(seats where racing and seat.cut_order == 0 by standing)) {
      show seat, WARNING;
      if ((next_cut - race.steps) % 60 == 0) { stock_banner seat, TICK; }
    } else {
      hide seat, WARNING;
    }
  }
}

rule "Places" {
  // the places shown during the race, the results' order: the racers still in (not cut) by the race order, then the
  // racers cut or out, the last out first (the first out is last). A cut racer still tumbling is already below the rest
  event every step;
  while race.started;
  do {
    int n = 1;
    for each seat where seat.cut_order == 0 by standing { seat.place = n; n += 1; }
    for each seat where seat.cut_order > 0 by seat.cut_order desc { seat.place = n; n += 1; }
  }
}

rule "Winner" {
  event every step;
  when race.started and count(seats where racing) <= keep;
  do {
    // a player still in has won through: the same FINISH! and place as a line crossed, then the results
    for each seat where seat.racing and seat.is_human {
      seat.place = seat.standing;
      finish_seat seat;
      hold_until = race.steps + 8s;
    }
    if (hold_until > race.steps) { wait hold_until - race.steps; }
    // survivors first (end_race fills them in standing order), then the racers out, last out first
    int n = keep + 1;
    for each seat where seat.cut_order > 0 by seat.cut_order desc { seat.place = n; n += 1; }
    end_race;
  }
}

board seat cut_order "Out";
board global cut_in "Next cut";
```

## Limits

| Limit | Value |
|---|---|
| Api | The game version the file names; a released version's rules never change, and a game refuses a file of a newer version than its own |
| A file | 65,536 characters |
| The compiled mode, its text included | 65,536 bytes |
| Parameters | 256 |
| Rules; subs | 256; 128 |
| Globals; words a seat; words a team | 256; 64; 32 |
| Locals a rule (a vec takes 3) | 64 |
| Locals a waiting rule keeps (its event's seats, loop seats and a vec's three included) | 8 |
| Rules waiting at once (a rule on a seat counts 30) | 64 |
| Operations a step, at worst | 65,536 |
| Actions on the game a step (displays not counted) | 256 |
| Loops over the seats, nested (set helpers count) | 2 |
| Sorted loops and helpers, nested; keys a sort | 2; 8 |
| Nesting of brackets, blocks and expressions | 64 levels |
| `tag` texts; characters a text | 8; 10 |
| `results_title`, `results_heading` | 23 characters |
| Board lines a scope; label; words a line; bytes a word | 8; 32 bytes; 16; 16 |
| `points_table` places | 30 |
| Stat pack name | 64 characters |

The compiler counts the most operations a step could take, every branch counted, and refuses a mode past 65,536. An
`each seat` rule counts 30 times, a loop over the seats 30 times its body, a loop inside it 900; a set helper is a
loop; a `sqrt` counts 8, and `dist` holds one; a rule that waits counts twice. A file that breaks a limit while racing
ends the race for everyone at that step, with the results titled MODE ERROR.
