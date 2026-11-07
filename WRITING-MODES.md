# Writing game modes

A guide for players and lobby hosts who want to write their own Piconet game mode, a `.fzmode` file. It covers the
mode language of Piconet 0.6.0, **api 1**: what a mode is, a first mode built step by step, the parts of a file, every
word the language has, the official modes with notes, and the errors and limits.

Every example in a `fzmode` block below is a whole file that the real compiler accepts; the repository's tests compile
each one. The official modes in section 5 are quoted exactly as they ship.

**Contents**

1. [What a mode is](#1-what-a-mode-is)
2. [Your first mode](#2-your-first-mode)
3. [A file's layout](#3-a-files-layout)
4. [Reference](#4-reference)
5. [The official modes, annotated](#5-the-official-modes-annotated)
6. [Errors and limits](#6-errors-and-limits)

## 1. What a mode is

A mode is a text file, UTF-8, with the extension `.fzmode`. It sets a race's rules: the laps, the courses, how the
race starts and ends, what happens when a machine is hit or falls, who wins and what the results show. It does this
with a small closed vocabulary of the game's own mechanics: a mode can read and change only what the language names.

**It runs inside the race, on every PC.** The file is compiled once, and the compiled mode travels with the race
set-up to every player. Each player's game runs it at the same point of every race step (60 steps a second), and again
for every step that rollback runs a second time. It sees only the race, never the clock on the wall, so every game
works out the same race from the same inputs. A mode never makes one player's game differ from another's.

**It travels with the replay.** A replay holds the whole mode, its text included, and the values its settings had.
A replay of a modded race plays with no download, and shows what was raced.

**How a host races one.** Quick Play races only the official modes. In a custom lobby, the host picks the mode: on the
custom lobbies page, Create opens NEW LOBBY; under MODE, the server's official modes are listed first, then From file…
picks your own `.fzmode`. The mode's settings (its parameters, section 3.2) then appear beside it on their defaults,
and the host can change them while the lobby waits. A mode with no settings has nothing to change.

**The file is checked twice.** The launcher compiles the file when you pick it, and the server compiles it again with
the same compiler when the lobby is made. A file that does not compile is refused, and the launcher says why beside
the mode, with the line:

```text
Not a mode: line 12: 'lap_cuont' is not declared
```

A file of more than 65,536 characters is refused before it is read ("That file is too big for a mode."). The compiled
mode holds the file's text as well as its code, and must fit in 65,536 bytes too, so a file close to that size is
refused even when it is shorter.

**A mode that breaks during a race ends it for everyone.** If a running mode does something it may not (section 6.4),
the race ends at that step in every game alike, with the results as they stand. The results page is titled MODE ERROR,
and the launcher's race page says what went wrong. The replay reproduces it. The compiler refuses most such modes
before they race; the rest are rare.

**Words left out are the game's own way.** A file states every rule it changes; anything it leaves out is how the game
itself races, never a lobby's or a server's choice (section 3.3). A mode with only its header races 3 laps with no
CPUs.

**Versions.** Every file names the vocabulary it is written for: `api 1;`. A later Piconet adds words and never
changes what an existing word does, so an api 1 mode and its replays keep working. A build refuses an api newer than
its own.

Authoring is text only: write the file in any text editor.

## 2. Your first mode

This section builds one small, complete mode: a lap race with a rolling start, lives, host settings, a rule of its own
and a value on the launcher's race page. Each step adds one idea and shows the whole file so far.

### Step 1: the header

Every file starts with its header: the mode's name in quotes, then in braces the api and, optionally, a line saying
what the mode is.

```fzmode
mode "Sprint" {
  api 1;
  about "Three laps, three lives, a rolling start.";
}
```

This already races. With nothing else said, it is the game's own race: 3 laps, no CPUs, each racer's own machine
setting, the disc's stats, mines that stay gone once hit.

### Step 2: setup words

Setup words sit at the top level of the file and fix a rule for the whole race. Each ends with `;`.

```fzmode
mode "Sprint" {
  api 1;
  about "Three laps, three lives, a rolling start.";
}

laps 3;
cpus on;                    // players may vote CPUs into the race
stats "GX Unleashed";       // the server's stat pack, by its name
mine_regen on;              // a mine comes back 200 ms after it goes off
finish_limit 90;            // the race ends 90 seconds after the winner finishes
rolling_start 1000 km/h;    // every machine is already moving at the start
```

`//` starts a comment that runs to the end of the line. `finish_limit` matters: without it a mode waits for every
player to finish or be out.

### Step 3: lives

`lives` comes from the standard library (section 4.6). Every wreck the game restores costs a life; when one life is
left the machine is no longer restored, so its next wreck is final. The count shows beside the spare-machine icon.

```fzmode
mode "Sprint" {
  api 1;
  about "Three laps, three lives, a rolling start.";
}

laps 3;
cpus on;
stats "GX Unleashed";
mine_regen on;
finish_limit 90;
rolling_start 1000 km/h;
lives 3;
```

### Step 4: settings for the host

A `param` line declares a setting the host can change in the lobby: a name for the file, a label for the lobby, a
kind, a range and a default. A setup word can then take the parameter instead of a number. A race uses the
defaults unless the host changes them.

```fzmode
mode "Sprint" {
  api 1;
  about "Three laps, three lives, a rolling start.";
}

param lap_count  "Laps"  number 1..9 default 3;
param lives_each "Lives" number 1..9 default 3;   // 1: one wreck and out, the game's own rule

laps lap_count;
cpus on;
stats "GX Unleashed";
mine_regen on;
finish_limit 90;
rolling_start 1000 km/h;
lives lives_each;
```

A name is declared before it is used, so the `param` lines come before the words that take them.

### Step 5: a variable, a rule and the race page

A rule names an event, optional conditions, and what to do. This one counts the laps each racer has led. `seat int
laps_led;` keeps one whole number for every racer; `event lap(s)` runs once each time a racer completes a lap, with
`s` that racer; `when` is its condition. A `board` line puts the count on the launcher's race page.

```fzmode
mode "Sprint" {
  api 1;
  about "Three laps, three lives, a rolling start.";
}

param lap_count  "Laps"  number 1..9 default 3;
param lives_each "Lives" number 1..9 default 3;

laps lap_count;
cpus on;
stats "GX Unleashed";
mine_regen on;
finish_limit 90;
rolling_start 1000 km/h;
lives lives_each;

seat int laps_led;

rule "Lead a lap" {
  event lap(s);
  when s.standing == 1;      // first in the race order as the lap is done
  do { s.laps_led += 1; }
}

board seat laps_led "Laps led";
```

### Step 6: a rule that runs every step

`event each seat` runs once a step for every racer, with `seat` that racer. With `while`, the rule runs on every step
its condition holds. This one shows the game's warning sign to a racer on their last life. It reads `lives_left`, the
variable the `lives` line declares.

```fzmode
mode "Sprint" {
  api 1;
  about "Three laps, three lives, a rolling start.";
}

param lap_count  "Laps"  number 1..9 default 3;
param lives_each "Lives" number 1..9 default 3;   // 1: one wreck and out, the game's own rule

laps lap_count;
cpus on;                    // players may vote CPUs into the race
stats "GX Unleashed";       // the server's stat pack, by its name
mine_regen on;              // a mine comes back 200 ms after it goes off
finish_limit 90;            // the race ends 90 seconds after the winner finishes
rolling_start 1000 km/h;    // every machine is already moving at the start
lives lives_each;           // every restored wreck costs a life; the last wreck is final

seat int laps_led;

rule "Lead a lap" {
  event lap(s);
  when s.standing == 1;      // first in the race order as the lap is done
  do { s.laps_led += 1; }
}

rule "Last life" {
  event each seat;
  while race.started;
  do {
    if (seat.racing and seat.lives_left == 1 and lives_each > 1) {
      show seat, WARNING;
    } else {
      hide seat, WARNING;
    }
  }
}

board seat laps_led "Laps led";
```

That is a complete mode. Save it as `sprint.fzmode` and pick it with From file… in a new custom lobby.

## 3. A file's layout

A file is its header, then in any order: parameters, setup words, constants, variables, subs, rules and board lines.
A name must be declared above the line that uses it; subs are the exception and may be called from anywhere.

**Lexical rules.**
- Comments: `//` to the end of the line, and `/* … */` (not nested).
- Names: a letter or `_`, then letters, digits and `_`. Case matters. A keyword (below) cannot be a name.
- Whole numbers: `0`, `42`, `0x1F` (at most 8 hex digits). Reals: `1.5`, `0.25`, `2.` (a digit before the point is
  required: `.5` is refused).
- Percent: `50%` is the real 0.5.
- Times: `30s`, `500ms`, `2min` (no space before the unit) and `90 steps` (space or none). A time is a whole number of
  steps, 60 a second.
- Speeds and distances: `1000 km/h` and `300 m` (the space is required) are the reals 1000.0 and 300.0.
- Strings: `"…"`, on one line, with `\"` and `\\` as the only escapes. Strings are labels and names only: a mode
  cannot build or compare text.
- Multiply may be written `*` or `×`. Minus is `-`: the character `−` is refused.

**Keywords:** `mode api badge about param number delay switch choice default step const global seat team int real
bool seats sub rule event every each when while do board if else for in where by desc repeat wait abort restart call
return and or not true false none on off laps course macro race vec series carry`. `series` and `carry` are reserved
for later.

### 3.1 The header

```fzmode
mode "Chain reaction" {
  api 1;
  badge "CHAIN";
  about "An explosion takes everyone near it.";
}
```

- `mode "Name"`: the mode's name, as the lobby shows it.
- `api 1;`: required. The vocabulary the file is written for.
- `badge "WORD";`: optional. A short word for the mode, as on Quick Play's spicy vote cards.
- `about "…";`: optional. One line saying what the mode is, shown with its name.

### 3.2 Parameters: the host's settings

```text
param NAME "Label" number LEAST..MOST [step S] [or "Name"] [default D];
param NAME "Label" delay  LEAST..MOST [step S] [or "Name"] [default D];
param NAME "Label" switch [or "Name"] default on|off;
param NAME "Label" choice ["First", "Second", …] [or "Name"] [default "First"];
```

| Kind | Values | Read in a rule as |
|---|---|---|
| `number` | a range of numbers, `1..9`. A real when any of its numbers is written as one (`0.5..2.0`, `100%..400%`), else whole | an `int` or a `real` |
| `delay` | a range of times, `10s..2min`; every number of the line must be a time | an `int`: steps |
| `switch` | on or off; the default is required | a `bool` |
| `choice` | one of the names in the list | an `int`: the choice's place, 0 for the first |

- Without `default`, a number or delay starts at its least and a choice at its first name.
- `step` is the step between values the host can pick (above 0). For whole numbers and delays the default must lie on
  a step from the least.
- A parameter is a constant for the race: a rule can read it but never change it. The host's values travel with the
  race and the replay; the mode's identity (its hash) is the file's alone, whatever the values.
- At most 256 parameters.

```fzmode
mode "Settings" { api 1; }

param keep    "Racers left"   number 1..29 default 1;
param cut     "Cut every"     delay 10s..2min step 5s default 30s;
param power   "Damage"        number 50%..400% step 50% default 100%;
param rolling "Rolling start" switch default on;
param order   "Order"         choice ["Most KOs", "Last alive"] default "Most KOs";

damage power;

rule "Read them" {
  event race_start;
  do {
    int left = keep;
    int steps_between = cut;          // a delay is read in steps
    bool moving = rolling;
    if (order == 1) { left += 1; }    // "Last alive" is choice 1
  }
}
```

**A named value: `or "Name"`.** After a number or delay range, a switch or a choice, `or "Name"` gives the parameter one
value more that states nothing. A setup word given the parameter at that value is as if it were not written, so the
game's own way, or another form of the same word, stands instead. The default may be the name.

```fzmode
mode "Laps or table" { api 1; }

param lap_count  "Laps"          number 1..99 or "By course" default "By course";
param mines_back "Mines respawn" number 50..5000 step 50 or "Off" default 200;

laps by course {
  "Mute City: Sonic Oval": 8;
  else: 3;
}
laps lap_count;          // "By course": the table above; a number: that many laps everywhere
mine_regen mines_back;   // "Off": mines stay gone, the game's own way
```

Only these setup words take a parameter with a named value: `laps`, `laps by course`, `cpu_level`, `finish_limit`,
`mine_regen`, and the lobby's words `racers`, `teams`, `machine_setting`, `stats`, `mixed_packs`, `boost_start` and
`customs`. A rule cannot read such a parameter at all, since the name is no number. A real number range cannot have
one.

**Matching a parameter to a setup word.** A setup word checks a parameter's whole range against its own, and its type:
- A word that takes a real (`damage`, `rolling_start`, `start_gap`, `pedals locked`) needs a real parameter: write
  `500.0..2000.0`, not `500..2000`.
- A word that takes a whole number takes a whole `number`, a `delay` or a `choice` (its place, from 0). `cpu_level`
  with a choice of four names, as Quick Play's file does, gives 0 Novice to 3 Master.
- A word that takes on or off takes a `switch`. So do `mine_regen` and `time_bonus`, whose `on` is 200 ms and 5 s.
- A `delay` is steps. Give `delay` parameters to the words counted in time (`clock_limit`, `rewind`), and `number`
  parameters to `finish_limit` (seconds) and `mine_regen` (milliseconds).

### 3.3 Setup words

Setup words fix the race's rules before it starts; section 4.1 lists them all. General rules:
- Each word is written once. A word with two forms takes one of them (`machines all` or a list, `start_gap` or
  `start_gap even`), except beside a parameter with a named value: `laps by course { … }` and `laps lap_count;` together
  race the table at "By course" and the number otherwise.
- On-and-off words take `on`, `off` or a switch parameter, nothing else: `boost;`, `boost 1;` and `boost true;` are
  refused.
- A range word takes `least..most` or one value for both ends: `machine_setting 100;`. Either end may be a parameter:
  `machine_setting low..high;`.
- Whole numbers that are not times are written plain. `finish_limit` counts seconds and `mine_regen` milliseconds, so
  never give them a time: a time with a unit is steps, and `finish_limit 90s` is 5400 and refused.

**Left out, a word is the game's own way:**

| Left out | The race |
|---|---|
| `laps` | 3 laps |
| `cpus` | no CPUs |
| `cpu_level` | Master |
| `boost_start` | no boosting on the first lap |
| `mine_regen` | a mine that goes off stays gone |
| `machine_setting` | each racer's own setting |
| `stats` | the disc's own stats |
| `mixed_packs` | one pack for every racer |
| `finish_limit` | no finish limit: the race waits for every player |
| `restore` | no machine is restored; a wreck is out |
| `contact`, `boost`, the combat words | the game's own |
| `course`, `courses` | the lobby's vote, random draw or fixed course, over the whole course pool |
| `machines`, `customs` | every machine, custom machines too |
| `racers`, `teams` | the lobby's seats, no teams |

### 3.4 Constants and variables

```text
const  TYPE NAME = VALUE;       // fixed when the file compiles
global TYPE NAME [= VALUE];     // one for the race
seat   TYPE NAME [= VALUE];     // one for every seat: s.NAME
team   TYPE NAME [= VALUE];     // one for every team: s.team.NAME
```

The types:

| Type | Values |
|---|---|
| `int` | a 32-bit whole number |
| `real` | a 32-bit floating-point number |
| `bool` | `true` or `false` (`on` and `off` are the same) |
| `seat` | a racer's seat, 0 to 29, or `none` |
| `seats` | a set of seats |
| `vec` | three reals, x, y and z: a position, a direction or a motion; read with `.x`, `.y`, `.z` |

- A **seat** is a racer in this race, a player or a CPU. Seats are numbered from 0; `race.seats` is how many there are.
- A start value must be a constant. Without one a variable starts at 0, `false`, `none` or the empty set.
- `vec` is for locals only: a variable kept for the race cannot be a vec (keep its x, y and z as reals).
- A seat variable is read and written through a seat: `seat.lives_left`, `s.cut_order`. A team variable through a
  seat's team: `seat.team.pool`.
- A seat's team is set by the lobby before the race; a mode only reads it. Without teams, each seat's team is its own
  number.
- At most 256 globals, 64 words a seat and 32 words a team.

```fzmode
mode "Variables" { api 1; }

const  int  keep = 8;
global int  cuts = 0;
global seat leader;
global seats caught;
seat   int  lives_left = 3;
team   real pool;

rule "Use them" {
  event each seat;
  while race.started;
  do {
    if (seat.standing == 1) { leader = seat; }
    if (seat.lives_left < keep) { seat.team.pool += 1.0; }
    if (seat.out) { caught += seat; }
  }
}
```

### 3.5 Rules and when they run

```text
rule "Label" {
  event EVENT;              // exactly one
  when CONDITION;           // zero or more, all must hold
  while CONDITION;          // zero or more, only on every step and each seat
  do { STATEMENTS }
}
```

The label is optional and only names the rule in messages.

**Events that happen** (`game_start`, `countdown`, `race_start`, `lap`, `out`, `ko` and the rest, section 4.3) run the
rule once for each time they happen while its `when` conditions hold. `while` is refused on them. Their names in
parentheses bind the event's values: `event out(s, cause)`, `event ko(v, by)`, `event hit(a, b)`. A seat event written
without names binds `seat`.

**Events that go on** run every step:
- `event every step` runs once a step; `event each seat` runs once a step for every seat, with `seat` that seat.
- With `while`, the rule runs on every step its conditions hold.
- With `when`, it runs once, on the step its conditions become true, and not again until they have been false (for
  `each seat`, that is for each seat on its own).
- With both, it runs on `when`'s step if `while` holds then.
- With neither, it runs every step.

A rule with `when` whose body sets `seat.speed` or `seat.velocity`, or shows `show_clock` or `show_number`, compiles
with a warning, "did you mean while?": those words need writing every step.

**The three starts.**
- `game_start` comes first, once, before the first picture with the machines in it. What it does is in force from that
  picture on: use it for looks, the HUD's parts, numbers shown, energy, contact, restores and stats. No machine has
  driven yet, so the words that need a running step are refused there (`seat.speed`, `seat.velocity`, `push`, `boost`,
  `retire`, `explode`, `break_down`, `damage`, `finish_seat`, `cpu_mode`, `end_race`, `stop_all`, `stock_banner`)
  and so is `wait`.
- `countdown` is the first step the machines run: the 3-2-1 begins (with a rolling start, at once).
- `race_start` is GO. From then on `race.started` is true and `race.steps` counts up from 0.

**The order inside a step,** the same in every game:
1. The step's state is read and compared with the last step's to find the events.
2. The events that happen, by kind in this order: `countdown`, `race_start`, `out`, `ko`, `destroyed`, `fall`, `hit`,
   `damaged`, `wall`, `boost`, `pit`, `checkpoint`, `lap`, `finish`, `overtake`, `respawned`, `land`, `boost_end`,
   `wrong_way`. Within a kind, by seat number (a pair by its lower seat). For each, its rules in file order.
3. Rules whose `wait` has run out, in rule order, then seat order.
4. The rules on `every step` and `each seat`, in file order; an `each seat` rule seat by seat.
5. The actions on the game are applied.

**Reads and writes within a step.**
- The mode's own variables change at once.
- A read of the game sees the step as it stood when the mode began, for the whole step, even after an action on it:
  after `seat.energy = 50.0;`, `seat.energy` still reads the old value until the next step. The same goes for
  `seat.points` after `award` and `seat.place` after `seat.place = n`.
- Actions on the game are queued and applied after every rule has run, in order; their effect shows from the next
  step. Of two writes to the same thing in one step, the last wins.
- `end_race` comes after every other action of the step, so places written in the same step count.

A waiting rule (section 3.6, `wait`) does not run again for that seat until its wait is over.

### 3.6 Statements

```text
x = e;   x += e;   x -= e;   x *= e;   x /= e;
TYPE name = e;                                  // a local, until the block ends
if (c) { … } else if (c) { … } else { … }
for each seat [where c] [by key [desc], …] { … }
for each NAME in SET [where c] [by key [desc], …] { … }
repeat N { … }
wait T;   wait T abort when c;   wait T restart when c;
call name(args);
return e;                                       // in a sub
ACTION …;                                       // the actions of section 4.4 and 4.5
```

- Braces are always required after `if`, `else`, `for each` and `repeat`.
- **`for each seat`** walks the race's seats in seat order and binds `seat` (inside an `each seat` rule, the loop's
  seat hides the rule's). `for each s in SET` names its own seat. `where` skips seats; `by` sorts them, smallest first
  unless `desc`. Ties fall to the next key and last to the seat number. A `bool` key sorts `false` first. At most 8
  keys.
- **Bare reads.** Inside `where`, `by` and a set helper's parentheses, a read standing alone means the loop's seat:
  `for each seat where racing by standing`. Inside a loop's body, write `seat.racing`.
- **Loops are bounded.** Loops over the seats nest two deep at most (30 × 30), and a set helper (`count`, `first`,
  `min(… for each …)` and the rest) is such a loop too. Sorted loops and sorted helpers nest two deep. There is no
  `while` loop.
- **`repeat N`** takes a whole-number constant or parameter.
- **`wait T`** stops the rule here for T steps (a time or a whole number: `wait 3s;`, `wait n * 60;`); a T of 0 or less
  does nothing. The rule's locals are kept. `abort when c` drops the rest of the rule if c becomes true while it waits.
  `restart when c` starts the wait again on every step c is true, so it ends T steps after c was last true. A wait is
  refused in `game_start`, inside a sorted loop (`for each … by`), and in a sub used inside an expression. A waiting
  rule keeps at most 8 local slots (its event's seats, loop seats and a vec's three included).
- A local's name must not be taken by anything declared in the file.

```fzmode
mode "Statements" { api 1; }

param rounds "Rounds" number 1..5 default 3;

global int total;
seat int score;

rule "Show the statements" {
  event race_start;
  do {
    int n = 0;
    for each seat where racing by standing { n += 1; seat.score = n; }
    for each s in seats where s.is_human { award s, 1; }
    repeat rounds { total += 1; }
    if (total > 3) { total = 3; } else if (total < 0) { total = 0; } else { total *= 2; }
    wait 2s abort when count(seats where racing) < 2;
    for each seat { show seat, LEADER_GAP; }
  }
}
```

### 3.7 Expressions and types

Operators, loosest first:

| Operators | |
|---|---|
| `or` | either; stops at the first true |
| `and` | both; stops at the first false |
| `not` | |
| `==  !=  <  <=  >  >=  in` | one comparison at a time: write `a < b and b < c` |
| `+  -` | |
| `*  /  %  &` | `%` takes whole numbers only; `&` is the sets' intersection |
| `-` in front | |
| `.field`, calls | |

- **No hidden narrowing.** A whole number meets a real as a real. A real becomes a whole number only through `floor`,
  `ceil`, `round` or `trunc`.
- `bool`, `seat` and `seats` values compare with `==` and `!=` only.
- **Sets of seats.** `seats` is every seat of the race; `s.touching` the seats touching s this step. `+` is the union,
  `-` the difference, `&` the intersection; a seat on either side counts as its set. `s in SET` tests membership.
  `(SET where c)` is a set made by a condition: `(seats where seat.racing)`.
- **`none`.** A read of `none` gives 0, `false` or `none`; an action on `none` does nothing. Neither stops a race.
- **Vecs.** `seat.position`, `seat.velocity`, `seat.forward` and `seat.up`, or built with `vec(x, y, z)`. `a + b`,
  `a - b`, a vec times or divided by a number, a number times a vec; `-v`; `dot(a, b)`, `length(v)`, `cross(a, b)`.

**Functions.**

| Function | Gives |
|---|---|
| `abs(x)` | the size of x, of x's type |
| `floor(x)`, `ceil(x)`, `round(x)`, `trunc(x)` | a real made whole: down, up, nearest (a half away from zero), toward zero. A whole number is refused ("already an int") |
| `sqrt(x)` | the square root, as the game works it out; 0 for 0 or less |
| `min(a, b, …)`, `max(a, b, …)` | the least or most of two or more numbers |
| `random(n)` | a whole number from 0 to n − 1 (0 when n ≤ 0) |
| `random_real()` | a real from 0 up to, not including, 1 |
| `vec(x, y, z)`, `dot(a, b)`, `length(v)`, `cross(a, b)` | the vec functions above |

**Set helpers.**

| Helper | Gives |
|---|---|
| `count(SET where c)` | how many seats of SET pass c |
| `first(SET where c by key …)`, `last(…)` | the first or last seat in that order (seat order without `by`); `none` when none |
| `nth(SET, i)`, `nth(SET where c by key …, i)` | the seat at place i, from 0; `none` past the end |
| `sum(e for each seat where c)` | the sum of e over the seats that pass |
| `min(e for each seat where c)`, `max(…)` | the least or most of e; 0 when no seat passes |
| `any(e for each seat where c)`, `all(…)` | whether e holds for any, for all |

`where c` and `by …` are optional, and SET may be `seats` or any set. `for each NAME` names the seat as you like, so
the rule's own seat stays in sight: `min(gap_to(seat, other) for each other where other.racing)`. `for each NAME in SET`
walks only SET's seats.

```fzmode
mode "Expressions" { api 1; }

rule "Show the expressions" {
  event every step;
  while race.started;
  do {
    seat me = first(seats where racing by standing);
    int racing_now = count(seats where racing);
    seat back = last(seats where racing by standing);
    seat second = nth(seats where racing by standing, 1);
    int kos = sum(s.kos for each s);
    real closest = min(gap_to(me, other) for each other where other.racing and other != me);
    bool anyone_boosting = any(boosting for each seat);
    seats near = me.touching + me;
    bool touched = back in me.touching;
    vec ahead = me.position + me.forward * 100.0;
    real height = ahead.y;
    real across = length(cross(me.forward, me.up));
    int whole = floor(me.speed / 100.0);
    int dice = random(6) + 1;
    if (not me.boosting and (me.energy < 20.0 or me.lap >= race.laps - 1)) { show me, WARNING; }
  }
}
```

### 3.8 Numbers

The same numbers come out on every PC, so the arithmetic is fixed exactly:
- **Whole numbers** (`int`) are 32-bit. `+`, `-` and `*` wrap around. `/` drops the fraction toward zero, so `7 / 2` is
  3 and `1 / 2` is 0: write `1.0 / 2` or `0.5` for a half. `%` is the remainder, with the sign of the left side.
  Dividing by 0 gives 0, never an error.
- **Reals** are 32-bit floating point (binary32). Each `+ - * /` is rounded once, to nearest; `a * b + c` is two
  roundings. A whole number becomes a real exactly up to 16,777,216, and rounded beyond.
- **Real to whole:** only `floor`, `ceil`, `round` and `trunc`. A value too large becomes the largest whole number, and
  a result that is not a number becomes 0.
- **Not a number.** A real calculation that has no answer (0.0 / 0.0) gives a value that is not a number; every
  comparison with it is false. Written to the game, it stops the race (section 6.4).
- `sqrt` is the game's own square root, so a mode's distances agree with the game's. There is no `sin`, `cos`, `pow`
  or `exp` in api 1.
- **Times** are whole steps, 60 a second. A time that is not a whole number of steps is rounded to the nearest step
  (a half up), with a warning: `10ms` is 0.6 steps and becomes 1.
- **Random numbers** come from the mode's own generator, seeded from the match and the mode, never from the game's.
  Every race draws differently; every game in a race and its replay draw the same.

### 3.9 Subs

A sub is a named piece of code with arguments, for reuse. It may give a value.

```text
sub [TYPE] name(TYPE a, TYPE b, …) { … }
```

- Call one that gives a value inside an expression, `gap(a, b)`. Call any sub as a statement with `call name(…);`.
- `return e;` gives the value; `return;` leaves a sub that gives none.
- A sub sees only its own arguments and locals, and the file's names.
- A sub may not call itself, directly or through another.
- A sub is written out where it is called, so a large sub called often makes a large mode. At most 128 subs.
- The standard library's subs, `gap_to` and `dist`, are always there and cannot be declared again.

```fzmode
mode "Subs" { api 1; }

seat int score;

sub pay(seat s, int n) {
  award s, n;
  s.score += n;
}

sub real lead(seat a, seat b) {
  real d = a.distance - b.distance;
  if (d < 0.0) { return 0.0; }
  return d;
}

rule "Pay the leader" {
  event lap(s);
  when s.standing == 1;
  do {
    seat next = nth(seats where racing by standing, 1);
    if (lead(s, next) > 500 m) { call pay(s, 2); } else { call pay(s, 1); }
  }
}
```

### 3.10 The standard library

The standard library is written in the mode language and comes with every api: the macros `every`, `at`, `lives` and
`pits_off`, and the subs `gap_to` and `dist` (section 4.6 has the text of each). A macro is used at the top level of
the file, as a setup word or with a block:

```text
lives 3;
pits_off;
every(30s) { … }
at(4min) { … }
```

A macro becomes ordinary rules and variables where it is written, and costs nothing beyond them. Mode files cannot
declare macros of their own: a file reuses code with a `sub`.

### 3.11 The status board

A `board` line puts a variable on the launcher's race page, beside the race, for players and spectators. Nothing is
drawn in the game.

```text
board seat   NAME "Label";
board team   NAME "Label";
board global NAME "Label";
board seat   NAME "Label" ["Word for 0", "Word for 1", …];
```

- The variable must be declared above the line, in that scope, and not be a vec.
- With a list of words, the variable's value picks the word: 0 the first. An empty word shows nothing; a value past
  the list shows the number. A real variable cannot have words.
- At most 8 lines a scope; a label is 1 to 32 bytes; at most 16 words a line, 16 bytes a word.

```fzmode
mode "Board" { api 1; }

global int next_cut;
seat int state;          // 0 racing, 1 safe, 2 out
seat bool infected;
team real pool;

rule "Out" {
  event out(s);
  do { s.state = 2; }
}

board global next_cut "Next cut";
board seat state "State" ["Racing", "Safe", "Out"];
board seat infected "Status" ["", "Infected"];
board team pool "Tank";
```

## 4. Reference

Every word of api 1. Units: speeds in km/h, distances in metres along the course or in the course's space, times in
steps (60 a second) unless a word says seconds or milliseconds, energy as the game counts it (100 is a full machine
unless the mode changes it). Ranges are inclusive.

### 4.1 Setup words

#### The race and its field

| Word | Forms | What it does |
|---|---|---|
| `laps` | `laps 5;` `laps unlimited;` `laps p;` | Laps, 0 to 99. `unlimited` (0) is a race with no finish line: nobody finishes by laps |
| `laps by course` | `laps by course { "Mute City: Sonic Oval": 8; else: 3; }` | Laps by the course the race is on. A row names one of the 26 cup courses (section 4.7) and gives a number; `else:` is every other course and is 3 if left out. A row is a number, never a parameter. Not beside `laps` unless one of them has a parameter with a named value |
| `course` | `course "Big Blue: Drift Highway";` | Pins one course of the pool: no vote, no course choice in the lobby. Not with `courses`. Quick Play's rotation never takes a mode with it |
| `courses` | `courses all;` `courses ["Mute City: Sonic Oval", "Story: High-Stakes Jumps"];` | The courses the lobby's vote, random draw or fixed course may use, from the pool; `all` is the whole pool, as is leaving it out. In Quick Play each card draws one course of the list |
| `racers` | `racers 2..8;` `racers 4;` | The race's seat count, 1 to 30: the lobby's seats are the most; a race with fewer racers than the least is refused ("This mode needs at least 2 racers.") |
| `teams` | `teams 2..4;` `teams n..n;` | The race has teams, 2 to 30. The lobby has the most; a host chooses the count only when the mode gives it a parameter. A race with players on fewer teams than the least is refused |
| `cpus` | `cpus on;` `cpus off;` `cpus p;` | `on`: players may vote CPUs into the race. It never forces them in. Left out or `off`: no CPUs |
| `cpu_level` | `cpu_level 2;` `cpu_level p;` | The CPUs' level: 0 Novice, 1 Standard, 2 Expert, 3 Master (the default) |

#### Machines and stats

| Word | Forms | What it does |
|---|---|---|
| `machines` | `machines all;` `machines ["Blue Falcon", "Golden Fox"];` | The machines racers may choose and CPUs drive, by name (section 4.7). Left out: all |
| `customs` | `customs off;` `customs on;` | `off`: no custom machines; a racer who chose one races their stock machine. Left out: allowed |
| `machine_setting` | `machine_setting 100;` `machine_setting 50..100;` `machine_setting low..high;` | Each racer's machine setting is brought into this range, 0 to 100 |
| `stats` | `stats "GX Unleashed";` `stats stock;` `stats p;` | The race's stat pack, by its name; `stock` the disc's own stats. A server that does not hold the pack refuses the mode. With a choice parameter, each choice is a pack's name, or `stock`, or `file`: the host's own `.fzstats` file, which the launcher then asks for |
| `mixed_packs` | `mixed_packs on;` | Each racer may race a pack of their own. Left out: one pack for all |
| `max_energy` | `max_energy 150;` `max_energy p;` | Every machine's full energy, 1 to 1000, and every machine begins full. Left out: the game's 100 |

#### The start

| Word | Forms | What it does |
|---|---|---|
| `rolling_start` | `rolling_start 1000 km/h;` `rolling_start;` | The race starts rolling: from the first picture every machine moves at this speed (0 to 9000; 1000 left out), with full energy, a CPU steering every machine through the countdown at 60% accelerator with no boost or attacks. Every pad takes over at GO |
| `start_gap` | `start_gap 200 m;` `start_gap 0;` `start_gap p;` | Machines start this many metres apart, going back from the line in grid order across three lanes (0 to 10,000; at most the lap over the racers, so the field fits one lap). `0` puts everyone on one spot with contact off from the first step: `contact on` is refused beside it. Above 0 and below 12.9 m (the game's own grid step) needs `contact off`. Every machine starts behind the line, and its first crossing counts no lap |
| `start_gap even` | `start_gap even;` | The machines spread evenly round the whole lap behind the line. Not with `start_gap` |
| `boost_start` | `boost_start on;` | Boosting is allowed on the first lap. Left out: not, the game's way |

#### Driving and contact

| Word | Forms | What it does |
|---|---|---|
| `pedals locked` | `pedals locked;` `pedals locked accel 0.8 brake 0.0;` | Every machine's accelerator and brake are held (0 to 1; accel 1.0 and brake 0.0 if left out), CPUs too. Steering, strafe, spin and side attacks stay the driver's. An accelerator below 0.5 cancels side attacks; 0, or any brake, refuses every boost |
| `boost` | `boost off;` | No machine can start an energy boost. A dash plate still boosts |
| `contact` | `contact off;` | Every machine passes through every other |
| `restore` | `restore on;` | Every machine is restored by the game after a wreck instead of going out (a fall, an explosion, running out of energy; never time over). Left out: no restores. `respawns s off` takes one seat out of it |
| `mine_regen` | `mine_regen 200;` `mine_regen on;` `mine_regen off;` `mine_regen p;` | A mine comes back this many milliseconds after it goes off, 50 to 5000; `on` is 200. Left out or `off`: it stays gone |

#### Combat

| Word | Forms | What it does |
|---|---|---|
| `damage` | `damage 400%;` `damage 2.0;` `damage p;` | Machine contact's damage, 10% to 1000% (0.1 to 10.0); the game's cap of 20 a hit for a player moves with it. Walls, damage strips and mines are not machine contact |
| `knockback` | `knockback 150%;` | Machine contact's push, 10% to 1000%, the attacks' push with it |
| `wall_damage` | `wall_damage 50%;` | A wall's damage, 10% to 1000% |
| `wall_bounce` | `wall_bounce 200%;` | A wall's bounce, 10% to 1000%. The game's bounce acts only above about 400 km/h |
| `spin_attacks` | `spin_attacks off;` | No machine spin-attacks, CPUs included |
| `side_attacks` | `side_attacks off;` | No machine side-attacks |
| `spin_immunity` | `spin_immunity on;` | A spinning machine takes no contact damage |
| `damage_cap` | `damage_cap off;` | A player's hit is no longer held to 20 (to 20 × `damage` for contact), as a CPU's never is. A hit still takes at most 1.01 × the full energy, and walls and mines keep their own caps |

All of the combat values can also change during the race (section 4.4, `race.damage = …`).

#### The end and the results

| Word | Forms | What it does |
|---|---|---|
| `finish_limit` | `finish_limit 90;` `finish_limit p;` | The race stops waiting for the machines still racing this many seconds after the first finish, 0 to 600 (0: never). The results follow once the last player to finish has had FINISH! on screen, 8 seconds at most, and a machine that crosses the line in that wait still finishes. Left out: the race waits for every player |
| `clock_limit` | `clock_limit 5min;` `clock_limit p;` | The game's timed race: at this time from GO the race ends, with places by the race order unless the mode gave them. 1 second to 18 min 12 s (60 to 65,535 steps). The clock is not drawn unless the mode shows it: `show s, TIMER`. Not with `show_clock` |
| `time_bonus` | `time_bonus on;` `time_bonus 10;` `time_bonus off;` | With `clock_limit`: the first machine past each new quarter of the race's laps adds this many seconds (1 to 120; `on` is 5) to the one clock, and TIME BONUS shows |
| `race_waits everyone` | `race_waits everyone;` | The race goes on after the last player is out, until the mode ends it or every machine is finished or out. Needs `end_race` in a rule or `clock_limit` |
| `points_table` | `points_table game;` `points_table [10, 6, 4, 3, 2, 1];` | What each place earns when the mode ends the race, added to `award`'s: `game` is the Grand Prix's (100, 93, 87 … 15 for places 1 to 30); a list gives places 1 on, at most 30 numbers of 0 to 999. Numbers only. Opens the points page. Needs `end_race` or `clock_limit` |
| `results points` | `results points;` | The times page passes by itself and the results are the points page. Needs `award` or `points_table` |
| `results_title` | `results_title "KNOCKOUT";` | The points page's title in place of VS TOTAL RANKING. A to Z and the space only, 1 to 23 characters, no space at either end. A mode with a title ends on the points page, every row 0 without `award` |
| `results_heading` | `results_heading "OVERDRIVE";` | Heads the times list of a mode with no points page. The same letters and length. Refused beside `award`, `results_title` or `points_table`. Left out, a mode's times list is headed RESULTS |
| `hud clean` | `hud clean;` | Nothing over the race but the HUD's own parts: no CHECK sign and no name tags, a player's or a `tag`. For footage |

How a race ends, then: by laps and `finish_limit` as the game does; at `clock_limit`; when a rule says `end_race`; or
when the last player is finished or out (unless `race_waits everyone`). The results are the game's own pages: the
times list, then, when the mode awards points or has a title or a points table, the points page ranked by points.

#### Races alone

`solo on;` makes a mode raced alone, one seat with no relay, started by the launcher itself, as Play's Practice is.
Its words are `rewind` (a time, 1 s to 5 min, that the player can go back through), `pause on` (Start stands the
picture; the stick and the bar move it within the rewind), `save_state on` (one saved moment: D-pad right saves, left
loads; needs `rewind`) and `resume_countdown` (0 to 5 s after a resume or a load). The compiler refuses these without
`solo on`, and `solo` beside `cpus`, `racers` or `teams`. No lobby races a solo mode ("This mode is raced alone, from
Play's Practice."), and Piconet 0.6.0 starts only its own Practice this way, so a host's solo file has no use yet.

#### Every setup word in a file

```fzmode
mode "Setup words: the field" { api 1; }

param lap_count "Laps"      number 1..99 or "By course" default "By course";
param skill     "CPU level" choice ["Novice", "Standard", "Expert", "Master"] default "Expert";
param pack      "Stats"     choice ["GX Unleashed", "stock", "file"] default "GX Unleashed";
param low       "Machine setting from" number 0..100 default 50;
param high      "Machine setting to"   number 0..100 default 100;

laps by course {
  "Mute City: Sonic Oval": 8;
  "Big Blue: Drift Highway": 6;
  else: 3;
}
laps lap_count;
courses ["Mute City: Sonic Oval", "Big Blue: Drift Highway", "Story: High-Stakes Jumps"];
racers 2..16;
teams 2..4;
cpus on;
cpu_level skill;
machines ["Blue Falcon", "Golden Fox", "Fire Stingray"];
customs off;
machine_setting low..high;
stats pack;
mixed_packs off;
max_energy 150;
rolling_start 900 km/h;
start_gap even;
boost_start off;
```

```fzmode
mode "Setup words: the race" { api 1; }

param dmg "Damage" number 50%..400% step 50% default 200%;

pedals locked accel 0.8 brake 0.0;
boost off;
contact on;
restore on;
mine_regen 500;
damage dmg;
knockback 150%;
wall_damage 50%;
wall_bounce 1.0;
spin_attacks on;
side_attacks off;
spin_immunity on;
damage_cap off;
finish_limit 60;
clock_limit 5min;
time_bonus on;
race_waits everyone;
points_table game;
results points;
results_title "SETUP WORDS";
hud clean;
```

```fzmode
mode "Setup words: one spot" { api 1; }

course "Big Blue: Drift Highway";
laps unlimited;
start_gap 0;
contact off;
machines all;
stats stock;
results_heading "ONE SPOT";
```

```fzmode
mode "Setup words: alone" { api 1; }

param lap_count "Laps" number 0..99 default 0;

solo on;
laps lap_count;
rewind 5min;
pause on;
save_state on;
resume_countdown 1s;
```

### 4.2 Reads

A read gives a value of the race as it stood at the start of this step (section 3.5). Seat reads are written on a seat:
`seat.speed`, `s.energy`, `first(seats).lap`.

#### A seat's progress

| Read | Type | What it gives |
|---|---|---|
| `.lap` | int | laps completed |
| `.distance` | real | metres along the course over all laps |
| `.lap_distance`, `.lap_length` | real | metres into this lap; the length of one lap |
| `.checkpoint`, `.checkpoint_fraction` | int, real | the course checkpoint it is at, and how far toward the next (0 to 1) |
| `.standing` | int | its place in the race order now, 1 first |
| `.wrong_way` | bool | the game says it is going the wrong way |
| `.race_time` | int | steps from GO to its finish, or to now while it races |
| `.mode_lap_steps` | int | steps since its last lap (or GO) |
| `.best_lap` | int | its fewest steps for a lap this race; 0 before its first |
| `.game_lap_steps` | int | the game's own clock of this lap |
| `.gap_ahead` | int | steps behind the racer one place ahead, timed where both passed (64 points a lap); -1 with nobody there |
| `.gap_behind` | int | steps ahead of the racer one place behind; -1 with nobody there |
| `.gap_leader` | int | steps behind the leader; 0 for the leader, -1 before anyone has a time. A racer a lap or more away reads as the time since it last passed here, less than the whole gap |

#### A seat's state

| Read | Type | What it gives |
|---|---|---|
| `.racing` | bool | neither finished nor out |
| `.finished`, `.out` | bool | it has finished; the game has booked it out |
| `.broken` | bool | broken down: out of energy |
| `.fallen` | bool | destroyed or off the course |
| `.destroyed` | bool | this life ended in an explosion |
| `.restoring` | bool | the game is restoring it after a wreck |
| `.spinning`, `.side_attack` | bool | a spin attack; a side attack |
| `.airborne` | bool | all four hover points are in the air |
| `.boosting` | bool | a boost is running (an energy boost or a dash plate) |
| `.boost_left` | int | steps left on the running boost: while `boosting`, `boost_left + 1` steps remain counting this one |
| `.in_pit`, `.used_pit` | bool | on a pit strip this step; has used a pit this race |
| `.surface` | int | what it is on: 1 pit strip, 2 dash plate, 3 jump plate, 4 ice, 5 dirt, 6 damage strip, 7 mine, 0 none of these |
| `.touching` | seats | the seats in contact with it this step |
| `.no_contact` | bool | nobody touches it |

#### Speed, energy and harm

| Read | Type | What it gives |
|---|---|---|
| `.speed` | real | km/h |
| `.top_speed` | real | its highest speed this race, as the results show it |
| `.energy` | real | its energy |
| `.max_energy` | real | its full energy: 100 unless the mode changed it |
| `.last_hit_energy` | real | the energy its last hit took |
| `.hits` | int | contact hits this race |
| `.damage_taken` | real | energy lost to damage this race |
| `.kos` | int | its KO count: the skulls the game credited it |

#### Who, and where

| Read | Type | What it gives |
|---|---|---|
| `.index` | int | the seat's number, 0 to 29 |
| `.is_human`, `.is_cpu` | bool | a person drives it; no person does |
| `.team` | int | its team, 0 to 29; without teams, its seat number |
| `.machine` | int | its machine, 0 to 40 (section 4.7), or 50 for a custom machine |
| `.place` | int | the place the mode gave it with `seat.place`, 0 none |
| `.points` | int | the points the mode gave it with `award` |
| `.position` | vec | where it is |
| `.velocity` | vec | its motion, in km/h |
| `.forward`, `.up` | vec | its nose and its up, each of length 1 |
| `.input.accel`, `.input.brake` | real | the accelerator and brake it drove with this step, 0 to 1 (a CPU's accelerator may pass 1) |
| `.input.steer`, `.input.strafe`, `.input.pitch` | real | its steering (-1 to 1), strafe and pitch this step |

#### A seat's stats

`.stat.NAME` is the stat the machine drives with this step; `.stat.base.NAME` its own value before the mode changed
it. All are reals. NAME is one of:

| Stat | What it is |
|---|---|
| `weight` | the machine's weight |
| `accel` | acceleration |
| `max_speed` | top speed |
| `grip` | grip in the corners |
| `turn` | turning |
| `strafe` | strafing |
| `drag` | drag |
| `boost_strength` | a boost's strength (a stat pack's value × 0.57) |
| `boost_length` | a boost's length in seconds |
| `body` | the damage a hit does to it, as a multiplier |
| `drift_rotation_resistance` | how much a sliding corner turns the machine |
| `drift_friction` | a sliding corner's grip |
| `turn_tension` | resistance to turning |
| `turn_reaction` | how far it leans into the steering |
| `strafe_turn` | the turn a strafe adds |
| `drift_accel` | drift acceleration |
| `turn_decel` | the speed lost to sliding sideways |
| `grip_window` | steps of full grip after the accelerator is newly full on the ground, 0 to 255 |
| `boost_energy` | the energy a boost takes each step (the game's 1/6; a stat pack may set its own) |

#### The race

| Read | Type | What it gives |
|---|---|---|
| `race.steps` | int | steps since GO, 60 a second |
| `race.started` | bool | GO has happened |
| `race.seats` | int | the race's machines |
| `race.laps` | int | the race's laps (0 unlimited) |
| `race.course` | int | the course's number (section 4.7) |
| `race.humans_left` | int | seats a person drives that are neither finished nor out |
| `race.human_finished` | int | seats a person drives that have finished |
| `race.damage`, `race.knockback`, `race.wall_damage`, `race.wall_bounce` | real | the combat values now: 1.0 is the game's own |
| `race.spin_attacks`, `race.side_attacks`, `race.spin_immunity` | bool | the combat switches now |

#### Every read in a file

```fzmode
mode "Reads" { api 1; }

rule "Progress and state" {
  event each seat;
  while race.started;
  do {
    int lap = seat.lap;
    real distance = seat.distance;
    real into_lap = seat.lap_distance;
    real lap_length = seat.lap_length;
    int checkpoint = seat.checkpoint;
    real toward_next = seat.checkpoint_fraction;
    int standing = seat.standing;
    bool wrong_way = seat.wrong_way;
    int race_time = seat.race_time;
    int this_lap = seat.mode_lap_steps;
    int best = seat.best_lap;
    int game_lap = seat.game_lap_steps;
    int ahead = seat.gap_ahead;
    int behind = seat.gap_behind;
    int to_leader = seat.gap_leader;
    bool racing = seat.racing;
    bool finished = seat.finished;
    bool out = seat.out;
    bool broken = seat.broken;
    bool fallen = seat.fallen;
    bool destroyed = seat.destroyed;
    bool restoring = seat.restoring;
    bool spinning = seat.spinning;
    bool side_attack = seat.side_attack;
    bool airborne = seat.airborne;
    bool boosting = seat.boosting;
    int boost_left = seat.boost_left;
    bool in_pit = seat.in_pit;
    bool used_pit = seat.used_pit;
    int surface = seat.surface;
    seats touching = seat.touching;
    bool no_contact = seat.no_contact;
  }
}

rule "Energy, who and where" {
  event each seat;
  while race.started;
  do {
    real speed = seat.speed;
    real top = seat.top_speed;
    real energy = seat.energy;
    real full = seat.max_energy;
    real last_hit = seat.last_hit_energy;
    int hits = seat.hits;
    real damage_taken = seat.damage_taken;
    int kos = seat.kos;
    int index = seat.index;
    bool human = seat.is_human;
    bool cpu = seat.is_cpu;
    int my_team = seat.team;
    int machine = seat.machine;
    int place = seat.place;
    int points = seat.points;
    vec position = seat.position;
    vec velocity = seat.velocity;
    vec forward = seat.forward;
    vec up = seat.up;
    real accel = seat.input.accel;
    real brake = seat.input.brake;
    real steer = seat.input.steer;
    real strafe = seat.input.strafe;
    real pitch = seat.input.pitch;
  }
}

rule "Stats" {
  event each seat;
  while race.started;
  do {
    real a = seat.stat.weight + seat.stat.accel + seat.stat.max_speed + seat.stat.grip + seat.stat.turn;
    real b = seat.stat.strafe + seat.stat.drag + seat.stat.boost_strength + seat.stat.boost_length + seat.stat.body;
    real c = seat.stat.drift_rotation_resistance + seat.stat.drift_friction + seat.stat.turn_tension;
    real d = seat.stat.turn_reaction + seat.stat.strafe_turn + seat.stat.drift_accel + seat.stat.turn_decel;
    real e = seat.stat.grip_window + seat.stat.boost_energy;
    real f = seat.stat.base.weight + seat.stat.base.accel + seat.stat.base.max_speed + seat.stat.base.grip;
    real g = seat.stat.base.turn + seat.stat.base.strafe + seat.stat.base.drag + seat.stat.base.boost_strength;
    real h = seat.stat.base.boost_length + seat.stat.base.body + seat.stat.base.drift_rotation_resistance;
    real i = seat.stat.base.drift_friction + seat.stat.base.turn_tension + seat.stat.base.turn_reaction;
    real j = seat.stat.base.strafe_turn + seat.stat.base.drift_accel + seat.stat.base.turn_decel;
    real k = seat.stat.base.grip_window + seat.stat.base.boost_energy;
  }
}

rule "The race" {
  event every step;
  while race.started;
  do {
    int steps = race.steps;
    bool started = race.started;
    int seat_count = race.seats;
    int lap_total = race.laps;
    int course_number = race.course;
    int humans_left = race.humans_left;
    int humans_done = race.human_finished;
    real damage = race.damage + race.knockback + race.wall_damage + race.wall_bounce;
    bool attacks = race.spin_attacks and race.side_attacks and not race.spin_immunity;
  }
}
```

### 4.3 Events

| Event | Binds | When |
|---|---|---|
| `game_start` | nothing | once, before the first picture with the machines in it; the words that need a running step and `wait` are refused here |
| `countdown` | nothing | the first step the machines run: the 3-2-1 begins (with `rolling_start`, at once) |
| `race_start` | nothing | GO |
| `every step` | nothing | every step |
| `each seat` | `seat` | every step, for each seat in seat order |
| `lap(s)` | a seat | s completed a lap |
| `checkpoint(s)` | a seat | s reached another checkpoint |
| `finish(s)` | a seat | s finished |
| `out(s, cause)` | a seat, an int | the game booked s out. `cause`, the first that holds: 5 time over; 2 exploded (contact, a damage strip, a death surface, `explode`); 3 fell off the course; 1 out of energy, at rest; 4 retired; 0 none of these |
| `fall(s)` | a seat | s left the course with no explosion |
| `destroyed(s)` | a seat | s exploded, once for each life. It may come after s's `out`: a wreck at rest explodes when another machine touches it |
| `ko(v, by)` | a seat, a seat | v's contact KO began. `by` is the machine the game credited (its KO count rose this step), or `none`: a wall, a strip or a mine credits nobody, and neither does a step with two KOs |
| `hit(a, b)` | a seat, a seat | a and b began touching; a has the lower seat number |
| `damaged(s)` | a seat | s's energy fell while it was not boosting |
| `boost(s)`, `boost_end(s)` | a seat | a boost began; a boost ended (energy boost or dash plate) |
| `wall(s)` | a seat | s began touching a wall |
| `pit(s)` | a seat | s came onto a pit strip |
| `overtake(a, b)` | a seat, a seat | a, racing, passed b, racing, in the race order |
| `respawned(s)` | a seat | s's restore is over: it drives again |
| `land(s)` | a seat | s came down from the air |
| `wrong_way(s)` | a seat | s began going the wrong way |

The names in parentheses are yours: `event ko(victim, killer)` works as well. A seat event written without names
binds `seat`.

```fzmode
mode "Events" { api 1; }

global int events;

rule { event game_start; do { events += 1; } }
rule { event countdown; do { events += 1; } }
rule { event race_start; do { events += 1; } }
rule { event every step; while race.started; do { events += 0; } }
rule { event each seat; while seat.racing; do { events += 0; } }
rule { event lap(s); do { events += 1; } }
rule { event checkpoint; do { events += seat.checkpoint * 0; } }
rule { event finish(s); do { events += 1; } }
rule { event out(s, cause); when cause == 3; do { events += 1; } }
rule { event fall(s); do { events += 1; } }
rule { event destroyed(s); do { events += 1; } }
rule { event ko(v, by); when by != none; do { award by, 1; } }
rule { event hit(a, b); do { events += 1; } }
rule { event damaged(s); do { events += 1; } }
rule { event boost(s); do { events += 1; } }
rule { event boost_end(s); do { events += 1; } }
rule { event wall(s); do { events += 1; } }
rule { event pit(s); do { events += 1; } }
rule { event overtake(a, b); do { events += 1; } }
rule { event respawned(s); do { events += 1; } }
rule { event land(s); do { events += 1; } }
rule { event wrong_way(s); do { events += 1; } }
```

### 4.4 Actions

An action is a statement. Its seat comes first; commas between its values are optional (`cpu_mode s, 4;` and
`cpu_mode s 4;` are the same). Words written as `seat.NAME = value;` also take `+=`, `-=`, `*=` and `/=` where the
value is a number. Actions take effect from the next step (section 3.5).

#### Moving

| Action | What it does |
|---|---|
| `seat.speed = v;` | Holds the seat's speed at v km/h for the next step's travel, 0 to 9000. Write it every step to keep it (a `while` rule). A seat frozen, broken down, destroyed, finished, retired or restoring is not held. Long falls get steeper and jumps lower at a held speed; boosting still costs energy |
| `seat.velocity = v;` | Sets the seat's motion to the vec v, in km/h, for one step's travel; from the next step the game's forces act on it again |
| `push s, v;` | Adds the vec v, in km/h, to the seat's motion in the next step; the game's forces act on it from then on |
| `freeze s;` `release s;` | The seat stands still (it can still turn) until released |
| `boost s;` | Asks for the game's own energy boost, with all the game's rules: refused on the first lap (without `boost_start`), with too little energy, with the brake on or the accelerator at 0, or with a boost running. A refused request lasts that step only |
| `boost s off;` | The seat can no longer boost, to the race's end. A running boost and a dash plate are not stopped |

#### Energy and harm

| Action | What it does |
|---|---|
| `seat.energy = v;` | Sets the seat's energy, held to 0 to 101% of its full energy |
| `seat.max_energy = v;` | The seat's full energy, 1 to 1000 (a fraction is dropped). The energy stays where it is unless it is above the new full; then it is the new full. To fill too, write both with the same number: `s.max_energy = 150; s.energy = 150;` |
| `damage s, x;` | The game's own damage: x times the machine's `body`, at most 20 for a player (unless `damage_cap off`) and 1.01 × full energy; nothing on a finished or restoring seat, and x below 0 counts as 0. Below 0 energy the machine breaks down as after a last hit. Credits no KO. Several in one step add up |
| `break_down s;` | The seat runs out of energy as after a last hit: it tumbles, and is out by the game's own verdict when it comes to rest, explodes or falls. `ko` fires for it |
| `explode s;` | The seat explodes and is out at the next step, or is restored if it respawns. Credits no KO. Nothing on a finished, restoring or already destroyed seat |
| `danger_zone s, below, arm, seconds;` | The story's speed bomb: armed once the seat has reached `arm` km/h; below `below` km/h a count of `seconds` (1 to 9) runs down, and at its end the seat explodes. Show the count with `show s, DIGIT` |
| `danger_zone s off;` | The seat carries no bomb; a count under way stops |

#### Stats

`seat.stat.NAME = v;` (and `+=`, `-=`, `*=`, `/=`) changes a stat from the next step, for any of the stats in section
4.2, held to the range a stat pack may use. It lasts until written again or the race ends, a restore included. To give
the machine its own back, write `seat.stat.NAME = seat.stat.base.NAME;`. The stats read when a boost starts
(`boost_strength`, `boost_length`) change from the next boost; `body` at the next hit; `grip_window` at the next fresh
press of the accelerator; a CPU keeps the `drift_accel` it had when its race began. `.stat.base` cannot be written.

#### Out, restores and finishing

| Action | What it does |
|---|---|
| `retire s;` | The seat retires: the game books it out at the next step. Not on a finished seat |
| `respawns s;` | The game restores the seat after a wreck instead of booking it out: a fall, an explosion, running out of energy; never time over |
| `respawns s off;` | The seat's next wreck is final (a restore under way finishes) |
| `finish_seat s;` | The seat finishes here and now, as if it had crossed the line: FINISH!, its place, and the CPU drives it on. A place the mode gave it stays. Not on a finished or out seat |

There is no word to restore a healthy machine on demand: write `respawns s;` and `explode s;`.

#### Control and contact

| Action | What it does |
|---|---|
| `hand_to_cpu s;` | The CPU drives the seat from the next step |
| `pads_back;` | Every seat handed to the CPU drives on its pad again |
| `cpu_mode s, n;` | A CPU seat drives one of the story mode's scripted drivers, n 1 to 33; 0 is the normal driver. Nothing on a player's seat. Not every n's driving is known |
| `no_contact s;` `no_contact s off;` | Nobody touches the seat, and it touches nobody; and back. Bring contact back only when the seats are apart: a pair that still overlaps may be pushed apart hard |
| `no contact a, b;` | Every seat of a passes through every seat of b; each side a seat or a set |
| `team t no contact;` | Teammates of team t pass through each other and still bump everyone else |

#### Combat during the race

| Action | What it does |
|---|---|
| `race.damage = v;` `race.knockback = v;` `race.wall_damage = v;` `race.wall_bounce = v;` | The setup words' values from the next step, held to 10% to 1000% |
| `race.spin_attacks = b;` `race.side_attacks = b;` `race.spin_immunity = b;` | The switches, from the next step: `on`, `off` or any bool |

#### Results

| Action | What it does |
|---|---|
| `seat.place = n;` | The seat's place in the results when the race ends, 1 first (0: none). When the mode ends the race, the seats it placed keep their places, every other racing seat follows in race order, and a placed seat that went out shows the time it went out |
| `award s, n;` | Adds n points to the seat (n may be negative). The results show a negative total as 0 |
| `end_race;` | The race ends for everyone at this step, after every other action of the step |
| `stop_all;` | Every machine brakes. It ends nothing, so a mode with it needs `end_race` or `clock_limit` |

#### Every action in a file

```fzmode
mode "Actions" { api 1; }

seat int bombs;

rule "Moving" {
  event each seat;
  while race.started and seat.racing and seat.lap == 0;
  do {
    seat.speed = 1100.0;
    seat.velocity = seat.forward * 1100.0;
    push seat, cross(seat.forward, seat.up) * 20.0;
  }
}

rule "Once on the last lap" {
  event each seat;
  when race.started and seat.racing and seat.lap == race.laps - 1;
  do {
    freeze seat;
    release seat;
    boost seat;
    boost seat off;
    seat.energy = 50.0;
    seat.energy -= 5.0;
    seat.max_energy = 150;
    damage seat, 5.0;
    danger_zone seat, 500.0, 800.0, 3;
    danger_zone seat off;
    seat.stat.max_speed = seat.stat.base.max_speed * 1.1;
    seat.stat.grip *= 1.2;
    seat.stat.boost_energy = seat.stat.base.boost_energy * 0.5;
    respawns seat;
    respawns seat off;
    hand_to_cpu seat;
    pads_back;
    cpu_mode seat, 0;
    no_contact seat;
    no_contact seat off;
    no contact seat, seats - seat;
    team seat.team no contact;
    award seat, 1;
    seat.place = seat.standing;
  }
}

rule "Wrecks" {
  event hit(a, b);
  do {
    if (a.kos > 3) { explode a; }
    if (b.kos > 3) { break_down b; }
  }
}

rule "Too slow" {
  event each seat;
  when race.started and seat.racing and seat.speed < 100.0 and race.steps > 1min;
  do { retire seat; }
}

rule "The combat" {
  event race_start;
  do {
    race.damage = 2.0;
    race.knockback *= 2;
    race.wall_damage = 50%;
    race.wall_bounce += 1;
    race.spin_attacks = off;
    race.side_attacks = not race.side_attacks;
    race.spin_immunity = on;
  }
}

rule "The end" {
  event every step;
  when race.started and race.steps >= 5min;
  do {
    stop_all;
    for each seat where seat.racing and seat.is_human { finish_seat seat; }
    end_race;
  }
}
```

### 4.5 Displays

The displays are the game's own: its HUD parts, banners and results, and Piconet's name tags and course map. A mode
cannot draw text or put things where it likes. Displays never change the race, and they take no place in a step's
queue of actions.

| Action | What it does |
|---|---|
| `show s, PART;` `hide s, PART;` | The seat's player sees or no longer sees a part of the race HUD. The standard parts are on until hidden; the others are off until shown |
| `show_number s, n;` | A whole number, two digits (07), beside the Grand Prix spare-machine icon: above 99 shows 99, a negative number hides it. A spectator sees the watched racer's |
| `show_number s off;` | Hides it |
| `stock_banner s, BANNER;` | The seat's player sees one of the game's banners once, as the game raises it |
| `show_clock n;` | The game's timed-race clock reads n steps, shown as seconds rounded up, red below 10. It ends nothing and must be written every step it should follow. Players see it with `show s, TIMER`. Not with `clock_limit` |
| `show_distance s, metres;` `show_distance s off;` | The DISTANCE part reads these metres, 0 to 99,999, instead of the metres to the finish; under 1000 drawn large in the middle |
| `show_objects s, n, of;` `show_objects s off;` | The OBJECTS part reads n/of, each 0 to 99 |
| `seat.look = LOOK;` | How everyone sees the seat's machine |
| `seat.own_look = LOOK;` | How the seat's own driver sees it (left unset: `seat.look`) |
| `seat.ghost_tint = 0xRRGGBBAA;` | A ghost's colour; 0 is Time Attack's yellow, the default |
| `seat.ghost_sound = on;` | A ghost keeps its engine sound; off by default, as in Time Attack |
| `seat.colour = 0xRRGGBBAA;` | The machine's body colour; 0 gives it its own back |
| `map_hide s from SET;` | s's dot is left off the course maps of the seats in SET; an empty set shows it to everyone |
| `map_mark s, 0xRRGGBB;` | s's dot's colour on every course map; -1 gives it its machine's colour |
| `tag s, "TEXT";` `tag s off;` | TEXT floats over the seat's machine in place of its name, for everyone else, CPUs too. A to Z, 0 to 9, the space, `.` and `-`; 1 to 10 characters, no space at either end; at most 8 different texts a mode. Written in quotes, never a variable |

**PART**, the HUD's parts:

| Part | |
|---|---|
| `RANK`, `LAPS`, `SPEED`, `ENERGY`, `LAP_TIMES`, `RACE_TIME`, `FACES` | the standard HUD: on until hidden |
| `TIMER` | the timed race's clock, with `clock_limit` or `show_clock` |
| `LEADER_GAP` | the game's gap to the leader at each lap line, for a second; the leader sees its lead when second place crosses |
| `GHOST_GAP` | Time Attack's live gap at the right edge, all race, against the seat furthest ahead: red while it is ahead |
| `DISTANCE` | REST and the metres to the finish, or `show_distance`'s |
| `OBJECTS` | the objects count, or `show_objects`'s |
| `DIGIT`, `WARNING`, `SPEED_GAUGE` | the story mode's big 3-2-1 digit, its warning sign and its speed gauge; `DIGIT` shows a `danger_zone`'s count |
| `SUCCESS`, `FAILED`, `TIME_OVER` | the story mode's MISSION CLEAR!, MISSION FAILED and "Time is up..." pictures |

**BANNER**: `FINAL_LAP`, `BOOST_POWER`, `TIME_BONUS`, `GAP` (the gap popup for the seat's last lap line), `WRONG_WAY`,
and `TICK`, the timed race's countdown beep.

**LOOK**: `normal`, the game's own; `ghost`, drawn as Time Attack draws its ghosts (see-through, no shadow, fading in
over a second); `invisible`, not drawn at all. A ghost keeps its dot on the course map unless the mode hides it.

```fzmode
mode "Displays" { api 1; }

rule "From the first picture" {
  event game_start;
  do {
    for each seat {
      hide seat, FACES;
      hide seat, RANK;
      show seat, GHOST_GAP;
      seat.look = ghost;
      seat.own_look = normal;
      seat.ghost_tint = 0x80FF80FF;
      seat.ghost_sound = on;
      seat.colour = 0xFF0000FF;
      map_hide seat from seats - seat;
      map_mark seat, 0x00FF00;
      show_number seat, 3;
      tag seat, "RIVAL";
    }
  }
}

rule "Every step" {
  event each seat;
  while race.started;
  do {
    show seat, TIMER;
    show seat, DISTANCE;
    show_distance seat, 1000.0 - seat.lap_distance;
    show seat, OBJECTS;
    show_objects seat, seat.lap, race.laps;
    if (seat.racing) { show_number seat, seat.lap; } else { show_number seat off; }
  }
}

rule "The clock" {
  event every step;
  while race.started;
  do { show_clock max(0, 3min - race.steps); }
}

rule "Banners" {
  event lap(s);
  do {
    stock_banner s, GAP;
    tag s off;
    show_distance s off;
    show_objects s off;
    s.look = normal;
  }
}
```

### 4.6 The standard library

These come with api 1. Their text, as the build carries it:

```text
// every(t) { ... }: runs on each multiple of t of the race clock after GO.
macro every(time t, block body) {
  rule { event every step; while race.started and race.steps > 0 and race.steps % t == 0; do body }
}

// at(t) { ... }: once, at t on the race clock.
macro at(time t, block body) {
  rule { event every step; when race.started and race.steps >= t; do body }
}

macro lives(int n) {
  seat int lives_left;
  rule { event game_start; do { for each seat { seat.lives_left = n; show_number seat, n; if (n > 1) { respawns seat; } } } }
  rule { event each seat; when seat.restoring; do { seat.lives_left -= 1; if (seat.lives_left <= 1) { respawns seat off; } } }
  rule { event out(s); do { s.lives_left = 0; } }
  rule { event each seat; do {
    if (seat.racing) { show_number seat, seat.lives_left; } else { show_number seat, -1; }
  } }
}

macro pits_off() {
  seat real pits_off_energy;
  rule { event each seat; do {
    if (seat.in_pit and seat.energy > seat.pits_off_energy) {
      seat.energy = seat.pits_off_energy;
    } else {
      seat.pits_off_energy = seat.energy;
    }
  } }
}

sub real gap_to(seat a, seat b) {
  real d = abs(a.lap_distance - b.lap_distance);
  return min(d, a.lap_length - d);
}

sub real dist(seat a, seat b) {
  real dx = a.position.x - b.position.x;
  real dy = a.position.y - b.position.y;
  real dz = a.position.z - b.position.z;
  return sqrt(dx * dx + dy * dy + dz * dz);
}
```

| Word | Written | What it does |
|---|---|---|
| `every` | `every(30s) { … }` `every(p) { … }` | The block runs at every multiple of the time after GO: 30 s, 60 s, 90 s… The time is at least one step: a time, or a `delay` parameter without a named value |
| `at` | `at(4min) { … }` | The block runs once, when the race clock reaches the time |
| `lives` | `lives 3;` `lives p;` | Every wreck the game restores costs one life: a hit, running out of energy, a fall, a breakdown. While a seat has more than one life the game restores it; the wreck that leaves it one turns its restores off, so its next wreck is final. The lives show beside the spare-machine icon while the seat races. It declares the seat variable `lives_left` (this life included; 0 once out), which rules and board lines can read. `lives 1` is the game's own rule |
| `pits_off` | `pits_off;` | The pit strips give no energy. A seat in a pit keeps the energy it came in with (less if it is hit there) |
| `gap_to(a, b)` | `real d = gap_to(seat, other);` | The distance along the course between two seats, wrapped at the finish line, the shorter way round |
| `dist(a, b)` | `real d = dist(seat, other);` | The straight-line distance between two seats. It uses `sqrt`, which costs 8 operations: in a loop over pairs, compare squares instead (section 6.5) |

Write `lives 3;` and `pits_off;` as shown: `lives(3);` and `pits_off();` are refused. A file that uses `lives` cannot
declare its own `lives_left`.

```fzmode
mode "Library" { api 1; }

param last_call "Last call" delay 1min..5min step 30s default 3min;

lives 3;
pits_off;

global int ticks;

every(30s) {
  ticks += 1;
  for each seat where racing { award seat, 1; }
}

at(last_call) {
  for each seat where racing and seat.lives_left == 1 { seat.energy = seat.max_energy; }
}

rule "Close racing" {
  event each seat;
  while race.started and seat.racing;
  do {
    real along = min(gap_to(seat, other) for each other where other.racing and other != seat);
    if (along < 20 m) { show seat, WARNING; } else { hide seat, WARNING; }
  }
}

rule "Nearest at GO" {
  event race_start;
  do {
    seat a = first(seats);
    seat b = last(seats);
    real apart = dist(a, b);
  }
}

board seat lives_left "Lives";
```

### 4.7 Courses and machines

**Courses.** `course`, `courses` and `race.course` use the course pool, the 30 courses Piconet races. `laps by course`
names the 26 cup courses; the four story stages take its `else`.

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

**Machines.** `machines` names them as below; `seat.machine` reads the number (50 for a custom machine).

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

## 5. The official modes, annotated

The official modes are mode files written with the same words as yours. Each is quoted whole, as it ships, followed by
notes on what its parts do. Quick Play's rotation is the normal mode (Quick Play) on most cards, with two spicy cards a
vote drawn from Knockout, Ghost race and Overdrive; a custom lobby's host may pick any of the five, Death Race included.

### 5.1 Quick Play (`competitive.fzmode`)

```fzmode
// Quick Play's normal rules: the game's own rules with the competitive scene's lap counts, and three lives each.
// One file with a lap table, not one file per course: the rules are the same on every course and only the laps
// differ, so a change to the rules is one edit and a vote card is (course, this mode).
// Every rule of the race is this file's (a word left out is the game's own way); the parameters are what a custom
// lobby's host may change, and their defaults are Quick Play's own race. Quick Play races them at their defaults.

mode "Quick Play" {
  api 1;
  about "Quick Play's rules: laps by course, GX Unleashed stats, three lives.";
}

param lap_count   "Laps"            number 1..99 or "By course" default "By course";   // a number: every course
param cpu_skill   "CPU level"       choice ["Novice", "Standard", "Expert", "Master"] default "Master";
param boost_first "Boost from the start" switch default off;
param mines_back  "Mines respawn"   number 50..5000 step 50 or "Off" default 200;  // milliseconds
param finish_in   "Finish limit"    number 0..600 step 5 default 90;                     // seconds after the winner; 0: none
param setting_low  "Machine setting from" number 0..100 default 100;
param setting_high "Machine setting to"   number 0..100 default 100;
param pack        "Stats"           choice ["GX Unleashed", "F-Zero GX+", "stock", "file"] default "GX Unleashed";
param cpu_racers  "CPU racers"      switch default on;   // on: players may vote CPUs in, as today; off: none
param lives       "Lives"           number 1..9 default 3;   // 1: the game's own rule, one wreck and out

cpus cpu_racers;
finish_limit finish_in;   // a mode owns its ending: Quick Play's own end, as today
cpu_level cpu_skill;
boost_start boost_first;
mine_regen mines_back;
machine_setting setting_low..setting_high;   // Quick Play: top speed, fixed
stats pack;       // a pack of the server's by name, stock (the disc's own) or file (the host's own pack file)
lives lives;      // every wreck (a hit, the energy, a fall, a breakdown) costs one, as in Death Race; the last is final

// The same table as ghost_race.fzmode's (keep the two alike; QuickPlayRotationTests checks they give the same laps on
// every course).
laps by course {
  "Mute City: Sonic Oval": 8;
  "Aeropolis: Screw Drive": 6;
  "Big Blue: Drift Highway": 6;
  "Casino Palace: Split Oval": 6;
  "Mute City: Twist Road": 6;
  "Green Plant: Mobius Ring": 4;
  "Outer Space: Meteor Stream": 4;
  else: 3;
}
laps lap_count;   // "By course": the table above
```

- **The parameters** are the custom lobby's settings when a host picks Quick Play, on Quick Play's own values. Every
  rule a host may change is a parameter; everything else is fixed.
- **`lap_count` with `or "By course"`.** At "By course", `laps lap_count;` is as if not written, and the `laps by
  course` table decides. Given a number, that number wins on every course.
- **`mines_back` with `or "Off"`.** At "Off", `mine_regen` is not written: mines stay gone, the game's way. The
  launcher shows the value with its unit, ms.
- **`cpu_skill`** is a choice of four names; `cpu_level` takes its place, 0 Novice to 3 Master.
- **`stats pack;`** names the server's pack by the choice, `stock` the disc's stats, `file` the host's own pack file.
- **`machine_setting setting_low..setting_high;`**: two parameters as a range's ends.
- **`lives lives;`**: the parameter named `lives` given to the library's `lives`. At 1 it is the game's own rule.
- **`finish_limit finish_in;`**: Quick Play's end, 90 seconds after the winner. Without it the mode would wait for
  every player.
- No rules of its own: setup words and the library's `lives` do everything.

### 5.2 Knockout (`knockout.fzmode`)

```fzmode
// A spicy Quick Play option: a rolling start, then the last racer is out at every cut.

mode "Knockout" {
  api 1;
  badge "KNOCKOUT";
  about "The racer in last place is out at every cut. A point for every cut you survive.";
}

param length    "Race length" delay 4min..10min step 30s default 7min;
param first_cut "First cut"   delay 20s..120s step 5s default 45s;   // a 30-machine grid is 374 m long
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
    interval = max(10s, (length - first_cut) / max(1, count(seats where racing) - keep - 1));
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

- **`laps unlimited;` and `race_waits everyone;`**: no finish line, and the race does not end when the last player is
  out. The mode ends it with `end_race` ("Winner").
- **"Arm"** works out the time between cuts once, at GO, from the race length, the first cut and the racers there are:
  `max(10s, …)` keeps it at least 10 seconds. `count(seats where racing)` is the racers at GO.
- **"Cut the last racer"** is a `while` rule, so it runs on every step its condition holds; it moves `next_cut` on, so
  the condition is false again on the next step. `last(seats where … by standing)` is the racer furthest back.
  `break_down v;` takes the machine's energy as a last hit does, and the game's own verdict puts it out. Every racer
  still in gets a point.
- **"Out on its own"**: a machine out by itself is ranked by when it went out, as a cut one is.
- **"Seconds to the cut"** writes `show_clock` every step and works out `cut_in`, the whole seconds left (`60.0` makes
  the division a real one, so `ceil` rounds up). **"Everyone sees the clock"** shows the TIMER part once, at GO, with
  `when`: a shown part stays shown.
- **"Warn the last racer"** shows the WARNING sign to the racer about to be cut, and a tick each whole second.
- **"Winner"**: when only `keep` racers are left, every player still in gets FINISH! and a place (`finish_seat`), the
  rule waits 8 seconds for FINISH! to show when it finished a player, then places the racers out, last out first, and
  ends the race. `end_race` places the survivors in race order ahead of them.
- **`results_title "KNOCKOUT";`** ends on the points page: a point for every cut survived.
- **The board**: the order out, and the seconds to the next cut, on the launcher's race page.

### 5.3 Ghost race (`ghost_race.fzmode`)

```fzmode
// A ghost race: a time trial raced together. Everyone starts on one spot and nobody touches anybody. Until the race starts
// you see only your own machine; from GO the others are ghosts, as Time Attack draws its ghosts. No place and no faces:
// the comparison is Time Attack's own live gap at the right edge, and the host may swap it for the game's gap to the
// leader at each lap line. The results are the game's own, by time. A spicy Quick Play option on any course of the
// pool, with Quick Play's laps by course.

mode "Ghost race" {
  api 1;
  badge "GHOST RACE";
  about "Race the same lap as everyone, through each other, for the best time.";
}

param lap_count "Laps" number 1..99 or "By course" default "By course";   // a number: every course
param map_dots "Ghosts on the map" choice ["Show", "Hide"] default "Show";   // Time Attack marks its ghosts on the map
param gap_kind "Gap" choice ["Live", "At lap lines", "Both", "None"] default "Live";

cpus on;          // players may vote CPUs in
stats "GX Unleashed";   // Quick Play's pack, mines and machine setting (a word left out is the game's own)
mine_regen on;
machine_setting 100;
start_gap 0;      // everyone on slot 0's spot
contact off;
// Quick Play's laps by course: the same table as competitive.fzmode's (keep the two alike; QuickPlayRotationTests
// checks they give the same laps on every course). Not a library macro: a mode file's laps change with a put to the
// mode store, a macro's only with a new build.
laps by course {
  "Mute City: Sonic Oval": 8;
  "Aeropolis: Screw Drive": 6;
  "Big Blue: Drift Highway": 6;
  "Casino Palace: Split Oval": 6;
  "Mute City: Twist Road": 6;
  "Green Plant: Mobius Ring": 4;
  "Outer Space: Meteor Stream": 4;
  else: 3;
}
laps lap_count;   // "By course": the table above

rule "Alone on the grid, no standings" {
  event game_start;
  do {
    for each seat {
      seat.look = invisible;
      seat.own_look = normal;
      hide seat, FACES;
      hide seat, RANK;
      if (gap_kind == 0 or gap_kind == 2) { show seat, GHOST_GAP; }
      if (gap_kind == 1 or gap_kind == 2) { show seat, LEADER_GAP; }
      if (map_dots == 1) { map_hide seat from seats - seat; }
    }
  }
}

rule "The others are ghosts" {
  event race_start;
  do {
    for each seat {
      seat.look = ghost;
    }
  }
}

rule "Whoever finishes is gone" {
  event finish(s);
  do { s.look = invisible; }   // the CPU drives a finished seat on: nobody should race that
}
```

- **`start_gap 0;` and `contact off;`**: everyone starts on the same spot and nobody touches anybody. `contact on` would
  be refused beside `start_gap 0`.
- **The same lap table as Quick Play**, with the same Laps setting.
- **"Alone on the grid, no standings"** runs at `game_start`, so its looks are in force from the first picture:
  everyone else is invisible, your own machine normal (`own_look`), the faces and the rank hidden, and the gap parts the
  host chose shown. `gap_kind == 0` is the first choice, "Live". With "Hide", each seat's dot is left off everyone
  else's course map: `seats - seat` is every seat but this one.
- **"The others are ghosts"**: from GO, every machine is drawn as a ghost to the others, and still normally to its
  driver.
- **"Whoever finishes is gone"**: a finished machine drives on under the CPU, so it is made invisible.
- No `award` and no title: the results are the game's own times list.

### 5.4 Overdrive (`overdrive.fzmode`)

```fzmode
// Overdrive (the game modes' design 9.2, the owner's of 29 October): a spicy Quick Play option. A rolling start at
// 1000 km/h, then the speed is locked and rises 10 km/h every 10 s, a little more every step. The accelerator, the
// brake and boost do nothing. Racers are out the normal way (their energy, off the course); the last one in wins.
//
// The speed (vocabulary 4.3): 1000 km/h at GO, 1060 at 1:00, 1180 at 3:00, 1360 at 6:00, 1600 at 10:00; the word
// caps at 9000. No clock: the race goes on until one machine is left (design 9.2).

mode "Overdrive" {
  api 1;
  badge "OVERDRIVE";
  about "Speed locked and rising. Last one alive wins.";
}

param rise "Speed added every 10 s" number 5..50 step 5 default 10;

cpus on;
stats "GX Unleashed";          // Quick Play's pack, mines and machine setting (a word left out is the game's own)
mine_regen on;
machine_setting 100;
laps unlimited;
pedals locked accel 1.0;       // at least 0.5, so side attacks still work
boost off;
rolling_start 1000 km/h;       // as Knockout: speed at the gate, CPU steers through the countdown, control at GO
race_waits everyone;           // the race goes on until the mode ends it, not until the last player is out
results_heading "OVERDRIVE";   // the times list: the winner first, then the racers out, last out first, each at its time

global int outs;
global int hold_until;         // a player's win: the race ends when the game's FINISH! and place have had their 8 s
seat   int out_order;          // 0: still in; 1: first out

rule "Lock the speed" {
  event each seat;
  while race.started and seat.racing;
  do { seat.speed = min(9000.0, 1000.0 + rise * race.steps / 600.0); }   // smooth: a little more every step
}

rule "Out" {
  event out(s);
  do { outs += 1; s.out_order = outs; }
}

rule "Last alive" {
  event every step;
  // a race of one ends when that racer is out
  when race.started and (count(seats where racing) == 0 or (race.seats > 1 and count(seats where racing) <= 1));
  do {
    // a player still in has won: the same FINISH! and place as a line crossed, then the results
    for each seat where seat.racing and seat.is_human {
      seat.place = 1;
      finish_seat seat;
      hold_until = race.steps + 8s;
    }
    if (hold_until > race.steps) { wait hold_until - race.steps; }
    // the last one in first (end_race places a CPU still racing), then the racers out, last out first
    int n = 1 + count(seats where seat.out_order == 0);
    for each seat where seat.out_order > 0 by seat.out_order desc { seat.place = n; n += 1; }
    end_race;
  }
}
```

- **`pedals locked accel 1.0;` and `boost off;`**: the accelerator, the brake and boost do nothing. An accelerator of
  at least 0.5 keeps side attacks working.
- **"Lock the speed"** writes `seat.speed` on every step (`while`), for each racing seat: 1000 km/h at GO and `rise`
  more every 10 seconds (600 steps), a little more each step. `rise * race.steps / 600.0` is a real because of `600.0`.
  The word itself stops at 9000; the `min` says so.
- **"Out"** numbers the racers as they go out, so two out in one step still have an order.
- **"Last alive"**: the race ends when one racer is left, or when the only racer of a race of one is out. The player
  still in gets FINISH! and first place, the rule waits 8 seconds, then places the others, last out first.
- **`results_heading "OVERDRIVE";`**: the game's times list headed OVERDRIVE, since there are no points.

### 5.5 Death Race (`death_race.fzmode`)

```fzmode
// Death Race, after F-Zero X's: Big Blue: Drift Highway, no finish line. Three lives each; the last wreck is final. The
// clock ends it, or the last machine standing. Points for every KO and for staying in, so leading alone does not win and
// neither does killing alone.
//
// Lives: any wreck (a hit, the energy, a fall) costs one. While a seat has more than one life the game restores it (its
// own restore, as with the lobby's Restore); on its last life nothing restores it, so the next wreck is out. The lives
// left show beside the Grand Prix spare-machine icon.
//
// Scoring, at the defaults (each one a parameter):
//   KO        3 for every KO the game credits (its own skull count, a restored victim's too); a fall, a wall or a mine
//             credits no one
//   survival  1 for every 30 s from GO while the seat is still in
//   the end   2 for every life left, and among the seats still in, by standing: 1st +5, 2nd +3, 3rd +1
// The last machine standing before the clock also gets the survival points the rest of the clock would have paid, so
// outliving everyone is never worth less than lasting to the clock.
//
// Balance at the defaults, 6 minutes:
//   | racer                                 | survival | lives | standing | KOs | total |
//   |---------------------------------------|----------|-------|----------|-----|-------|
//   | the leader, no wreck, 0 KOs           | 12       | 6     | 5        | 0   | 23    |
//   | mid-pack, one wreck, 3 KOs            | 12       | 4     | 0        | 9   | 25    |
//   | out at 3:00 with 6 KOs                | 6        | 0     | 0        | 18  | 24    |
// Results by points; ties by time survived, then KOs, then standing.
//
// Damage is 400% and the game's cap of 20 a hit for a player is off (a CPU never had it), so a hard hit can end a
// machine from full; a hit still takes at most 1.01 x max energy. Walls, mines and the pit are the game's own: the pit's
// refill (1.1 a step) cannot save a machine from one hit at this damage.
//
// Boosts take half the energy: every seat's boost energy is half its stat pack's (GX Unleashed's 0.15 a step, 0.075
// here; the game's 1/6 would be 1/12), from the start to the end, a restore included.

mode "Death Race" {
  api 1;
  badge "DEATH RACE";
  about "Drift Highway, no finish line. Three lives each. Boosts take half the energy. Points for every machine you take out and for staying in.";
}

param length      "Race length"         delay 3min..10min step 30s default 6min;
param dmg         "Damage"              number 100%..1000% step 50% default 400%;
param ko_points   "Points for a KO"     number 0..10 default 3;
param tick        "Survival every"      delay 10s..60s step 5s default 30s;
param tick_points "Points for survival" number 0..5 default 1;
param lives       "Lives"               number 1..9 default 3;
param life_points "Points a life left"  number 0..10 default 2;     // at the end, for the seats still in
param first_bonus  "1st alive"          number 0..20 default 5;    // the standing among the survivors at the end
param second_bonus "2nd alive"          number 0..20 default 3;
param third_bonus  "3rd alive"          number 0..20 default 1;

course "Big Blue: Drift Highway";
cpus on;                       // players may vote CPUs in as targets, as in F-Zero X
stats "GX Unleashed";          // Quick Play's pack, mines and machine setting (a word left out is the game's own)
mine_regen on;
machine_setting 100;
laps unlimited;                // no finish line
restore off;                   // the lobby's Restore: off; the lives below restore a seat on its own (respawns)
rolling_start 1000 km/h;       // as Knockout: speed at the gate, CPU steers through the countdown, control at GO
start_gap even;                // spread evenly round the lap behind the line, in grid order: no melee at GO
race_waits everyone;           // the race goes on until the mode ends it, not until the last player is out
damage dmg;
damage_cap off;
results_title "DEATH RACE";    // the points page, ties in the order below

global bool over;              // the race has ended: nothing scores from then on
global int hold_until;         // a player's last moment: FINISH! and its place have their 8 s
seat int score;                // the points awarded so far (seat.points read in the run that awards is the old value)
seat int kos_paid;             // the skull count already paid for
seat int lived;                // steps from GO to out; a survivor: the race's length
seat int lives_left;           // this life included; 0 once out
seat int rank;

sub pay(seat s, int n) {
  award s, n;
  s.score += n;
}

rule "KOs" {
  event each seat;
  while race.started and not over;
  do {
    // the game's own record: the skull count rises for the machine credited (two KOs in one step still count)
    if (seat.kos > seat.kos_paid) {
      call pay(seat, ko_points * (seat.kos - seat.kos_paid));
      seat.kos_paid = seat.kos;
    }
  }
}

rule "Lives" {
  event game_start;
  do {
    for each seat {
      seat.lives_left = lives;
      show_number seat, lives;
      if (lives > 1) { respawns seat; }
    }
  }
}

rule "Half-energy boosts" {
  // the seat's own value is its pack's; the mode's stays through a restore (the stat record is the mode's, not the machine's)
  event game_start;
  do {
    for each seat { seat.stat.boost_energy = seat.stat.base.boost_energy * 0.5; }
  }
}

rule "A life lost" {
  // a wreck the game restores: one life gone; with one left, the next wreck is final (the restore under way finishes)
  event each seat;
  when seat.restoring and not over;
  do {
    seat.lives_left -= 1;
    if (seat.lives_left <= 1) { respawns seat off; }
  }
}

rule "Out" {
  event out(s);
  when not over;
  do {
    s.lived = race.steps;
    s.lives_left = 0;
  }
}

rule "Lives shown" {
  // beside the spare-machine icon, the watched seat's; hidden once it is out
  event each seat;
  do {
    if (seat.racing) { show_number seat, seat.lives_left; } else { show_number seat, -1; }
  }
}

rule "Survival" {
  event every step;
  while race.started and not over and race.steps > 0 and race.steps % tick == 0 and race.steps <= length;
  do {
    for each seat where racing { call pay(seat, tick_points); }
  }
}

rule "The clock" {
  event every step;
  while race.started;
  do { show_clock max(0, length - race.steps); }
}

rule "Everyone sees the clock" {
  event each seat;
  when race.started;
  do { show seat, TIMER; }
}

rule "End" {
  event every step;
  when race.started and (race.steps >= length or (race.seats > 1 and count(seats where racing) <= 1));
  do {
    over = true;
    // the seats still in: their lives left, and before the clock the survival points the rest of it would have paid
    int rest = max(0, length / tick - race.steps / tick);
    for each seat where racing {
      seat.lived = length;
      call pay(seat, life_points * seat.lives_left + rest * tick_points);
    }
    int k = 0;
    for each seat where racing by standing {
      k += 1;
      if (k == 1) { call pay(seat, first_bonus); }
      else if (k == 2) { call pay(seat, second_bonus); }
      else if (k == 3) { call pay(seat, third_bonus); }
    }
    int n = 1;
    for each seat by seat.score desc, seat.lived desc, seat.kos_paid desc, standing {
      seat.rank = n;
      seat.place = n;
      n += 1;
    }
    // a player still in: the same FINISH! and place as a line crossed, then the results
    for each seat where seat.racing and seat.is_human {
      finish_seat seat;
      hold_until = race.steps + 8s;
    }
    if (hold_until > race.steps) { wait hold_until - race.steps; }
    for each seat { seat.place = seat.rank; }
    end_race;
  }
}

board seat score "Points";
board seat kos_paid "KOs";
board seat lives_left "Lives";
```

- **`course "Big Blue: Drift Highway";`** pins the course: the lobby has no course choice, and Quick Play's rotation
  never takes it.
- **`restore off;`** and lives rules of its own instead of the library's `lives`: the same rules, written out in the
  file with `not over`, so nothing changes once the race has ended.
- **`start_gap even;`** spreads the field evenly round the lap behind the line, with the rolling start: no melee at GO.
- **`damage dmg;` with `damage_cap off;`**: 400% contact damage by default, and a player's hit no longer held to 20, so
  a hard hit can end a machine from full.
- **"Half-energy boosts"** halves every machine's boost energy at `game_start`, from its own pack's value.
- **`sub pay`** awards points and keeps its own count in `score`, because `seat.points` read in the same step still
  gives the old total.
- **"KOs"** reads the game's own KO count (`seat.kos`) every step and pays for each new one, so two KOs in one step
  still count.
- **"Survival"** pays every racing seat on each multiple of `tick` until the clock's end.
- **"The clock"** and **"Everyone sees the clock"**: `show_clock` counts down from `length`, and the TIMER part shows it.
- **"End"**: at the clock's end, or with one machine left, it pays the survivors their lives and the survival points
  the clock would still have paid, then the standing bonuses, places everyone by points, then time survived, then KOs,
  then race order, gives the players still in FINISH!, waits 8 seconds, writes the places again from `rank` and ends
  the race.
- **`results_title "DEATH RACE";`** ends on the points page; the board shows points, KOs and lives on the race page.

## 6. Errors and limits

### 6.1 What the host sees

| When | What the launcher says |
|---|---|
| The file is more than 65,536 characters | That file is too big for a mode. |
| The file cannot be opened | That file can't be read. |
| The file does not compile | Not a mode: line N: the compiler's message |
| The mode needs more racers than the lobby has (`racers`) | This mode needs at least N racers. |
| The mode needs racers on more teams (`teams`) | This mode needs racers on at least N teams. |
| The mode is a solo mode | This mode is raced alone, from Play's Practice. |

The compiler stops at the first error. Fix it and pick the file again.

### 6.2 What the compiler refuses

Every message names its line: `line 12: …`. The common ones, as the compiler words them (NAME, WORD and the numbers
stand for your file's):

**Reading the file**

| Message | Cause |
|---|---|
| `'…' is not part of the language` | a character the language has no use for, such as `−` or `@` |
| `a string is not closed with " on its line` | |
| `a comment opened with /* is never closed with */` | |
| `'5x' is not a number: a time is written 30s, 500ms, 2min or 90 steps` | a number with a unit the language does not have |
| `a mode file starts with its header: mode "Name" { api 1; }` | |
| `the header names no api: api 1;` | |
| `api 2 is not one this build knows: its newest is api 1` | |
| `'…' is expected here, not …` | a missing `;`, `{` or `)`, and the like |
| `'WORD' is not a word of api 1` | an unknown setup word, or a statement that is no action and no assignment |
| `'WORD' is a word of the language and cannot name a variable` | a keyword used as a name |
| `presets are gone: a mode file is one choice, and a variant is a mode file of its own` | |
| `macros are the standard library's (api 1): a mode file reuses its code with a sub` | |

**Names and types**

| Message | Cause |
|---|---|
| `'NAME' is not declared` | a typo, or a name used above its declaration |
| `'NAME' is already declared` | |
| `'NAME' is a sub of the standard library's: name yours otherwise` | a sub named `gap_to` or `dist` |
| `'NAME' is kept per seat: read it as seat.NAME or s.NAME` | |
| `'racing' alone means a seat's read only where a loop names the seat: write seat.racing` | |
| `there is no seat here: an each seat or out rule, or a for each, binds one` | `seat` in a rule that binds none |
| `a seat has no 'NAME' (neither a read of api 1 nor a seat variable)` | |
| `race has no 'NAME': its reads are steps, started, seats, laps, …` | |
| `seat.NAME is the game's to say: it cannot be assigned` | a read with no action, such as `seat.lap = 2;` |
| `'NAME' is a parameter: the lobby sets it, a rule cannot` | |
| `WHAT takes a real, not a bool` | a value of the wrong type; with a real where a whole number is wanted it adds `: floor, ceil, round or trunc make an int of a real` |
| `'%' takes whole numbers: use floor, ceil, round or trunc first` | |
| `floor takes a real: its argument is already an int` | |
| `NAME may be "By course", which is no value: only a setup word takes it` | a rule reading a parameter with a named value |
| `show takes one of TIMER, DISTANCE, …` | a part, banner or look that is not one of the names |
| `'.lap_time' is called .mode_lap_steps now: write seat.mode_lap_steps` | an older name (`is_player`, `lap_time`, `lap_steps`, `ghost`, `event go`) |

**Setup words and parameters**

| Message | Cause |
|---|---|
| `laps is 0..99` | a value outside the word's range |
| `the parameter NAME's range goes outside WORD's 0..99` | |
| `WORD takes a real, and the parameter NAME is not one` | a whole-number parameter given to a word that takes a real, and the like |
| `WORD takes on, off or a switch parameter` | |
| `WORD is set twice` | |
| `WORD and WORD: one of them` | two forms of one word, such as `laps` and `laps by course` |
| `"Venue: Track" is no course of the pool ("Venue: Track", as courses.toml names them)` | in `course` or `courses`: a name spelt wrong, or a course outside the pool (section 4.7 lists them) |
| `"Venue: Track" is no course of the game's ("Venue: Track", as courses.toml names them)` | in `laps by course`: a name spelt wrong, or a story stage |
| `"…" is no machine of the game's` | |
| `laps by course names "…", which is not in this mode's courses` | |
| `WORD is the game's own: it takes no parameter with or "..." (only laps, cpu_level, finish_limit, mine_regen and the coordinator's words do)` | a named-value parameter given to a word that cannot take one |
| `NAME's default 7 is not on a step of 2 from 1` | |
| `NAME's default 12 is outside 1..9` | |
| `NAME is a switch: its default is on or off` | |
| `results_title: the game's font 0x1D draws A to Z and the space, not 'a'` | a title with a small letter, a digit or a sign |
| `tag: the game's font 5 draws A to Z, 0 to 9, the space, . and -, not 'a'` | |
| `start_gap below 12.9 m with contact on: the game's grid puts machines no nearer than that; give 12.9 m or more, 0, or contact off` | |
| `contact on: start_gap 0 keeps every racer on one spot, so contact stays off` | |
| `rewind is a word of a race raced alone: a mode with it says solo on` | |

**Words that need others**

| Message |
|---|
| `stop_all only brakes the machines: a mode with it needs end_race or clock_limit, or its race never ends` |
| `race_waits everyone waits for every machine: a mode with it needs end_race or clock_limit, or its race may never end` |
| `points_table pays when the mode ends the race: a mode with it needs end_race or clock_limit` |
| `time_bonus adds to the clock: a mode with it needs clock_limit` |
| `results points shows the points page: a mode with it needs award` |
| `results_heading heads the page of a mode with no points page: award, results_title and points_table end on the points page` |
| `show_clock and clock_limit both write the game's clock: a mode has one or the other` |

**Rules, statements and limits**

| Message | Cause |
|---|---|
| `'WORD' is not an event of api 1: every step, each seat, out, …` | |
| `while is for the events that go on (every step, each seat): lap happens once, so its rule takes when` | |
| `WORD needs a running step, and game_start comes before the first one: use countdown or race_start` | |
| `a wait counts steps, and game_start comes before the first one: use countdown or race_start` | |
| `a rule cannot wait inside a sorted loop (for each ... by): the order is the step's` | |
| `a waiting rule keeps at most 8 locals (its event's seat, the loops' and a vec's three included): here it would keep 9` | |
| `loops over the seats nest at most two deep (30 × 30)` | |
| `the sub NAME calls itself (NAME -> NAME): a sub may not, directly or through another` | |
| `the mode takes 70210 operations a step at worst, past api 1's 65536 (this rule takes 9000)` | the step budget (section 6.5) |
| `a mode has at most 64 rules waiting at once, a rule on a seat counted for every seat: with this one, 90 could` | |
| `this is nested too deep: a mode goes at most 64 levels (brackets, blocks, a run of operators, subs calling subs); put a part of it in a local` | |

Two of them in full, from real files:

```fzmode-refused
mode "Typo" { api 1; }

param lap_count "Laps" number 1..9 default 3;
laps lap_cuont;
```

Refused: `line 4: laps must be a constant`. A misspelt parameter is no parameter, so the setup word looks for a
constant of that name and finds none: when a setup word asks for a constant, check the parameter's spelling.

```fzmode-refused
mode "At the start" { api 1; }

rule "Too early" {
  event game_start;
  do { for each seat { seat.speed = 900.0; } }
}
```

Refused: `line 5: seat.speed needs a running step, and game_start comes before the first one: use countdown or race_start`

### 6.3 Warnings

A warning does not stop a file; the mode races as the warning says.

```fzmode-warns
mode "Warnings" { api 1; }

rule "Hold the speed" {
  event each seat;
  when race.started;
  do { seat.speed = 900.0; }
}
```

Warns: `line 3: rule "Hold the speed" has when, which runs it once, and uses seat.speed, which is meant for every step: did you mean while?`

The other two: a time that is not a whole number of steps (`line N: 10ms is 0.6 steps at 60 a second: it is taken as
1`), and an action given `none` written out (`line N: retire on none does nothing`).

### 6.4 When a running mode breaks

The race ends for everyone at that step, the results are titled MODE ERROR, and the launcher's race page gives the
reason:

| The race page says | What happened |
|---|---|
| Mode error: the race ended for everyone. The mode did too much in one step. | a step needed more than 65,536 operations. The compiler's count refuses such a mode before it races, so this should not happen |
| Mode error: the race ended for everyone. The mode asked for too many actions in one step. | more than 256 actions on the game in one step (displays not counted): 8 a seat for 30 seats |
| Mode error: the race ended for everyone. The mode gave the game a number it can't use. | a real that is not a number, or is infinite, written to the game: `seat.speed = 1.0 / 0.0;` |
| Mode error: the race ended for everyone. Too many of the mode's rules were waiting at once. | more than 64 rules waiting |
| Mode error: the race ended for everyone. The game had no room for the mode. | the game's memory had no room for the mode |
| Mode error: the race ended for everyone. The mode's code did something it may not. | a damaged mode; the compiler never writes one |

A division by zero, an action on `none` and a whole number that wraps around never break a race: each has a set
result (section 3.8).

### 6.5 Limits

| Limit | Value |
|---|---|
| A file | 65,536 characters |
| The compiled mode, its text included | 65,536 bytes |
| Parameters | 256 |
| Rules; subs | 256; 128 |
| Globals; words a seat; words a team | 256; 64; 32 |
| Locals a rule (a vec takes 3) | 64 |
| Locals a waiting rule keeps | 8 |
| Rules waiting at once (a rule on a seat counts 30) | 64 |
| Operations a step, at worst | 65,536 |
| Actions on the game a step | 256 (displays not counted) |
| Loops over the seats, nested | 2 (set helpers count) |
| Sorted loops and helpers, nested; keys a sort | 2; 8 |
| Nesting of brackets, blocks and expressions | 64 levels |
| `tag` texts; characters a text | 8; 10 |
| `results_title`, `results_heading` | 23 characters |
| Board lines a scope; label; words a line; bytes a word | 8; 32 bytes; 16; 16 |
| `points_table` places | 30 |
| Stat pack name | 64 characters |

**The step budget.** The compiler adds up the most operations a step could take, every branch of every `if` counted,
and refuses a mode above 65,536. An `each seat` rule counts 30 times, a loop over the seats 30 times its body, a loop
inside it 900, and a set helper is a loop. A `sqrt` counts 8, and `dist` holds one. A rule that waits counts twice.
To stay inside it:
- compare squared distances in a loop over pairs, not `dist` (`dot(apart, apart) <= reach * reach`);
- work a set out once into a `seats` local, not in a loop;
- keep `count`, `first` and the other helpers out of loops over the seats where you can;
- use `every step` with one `for each seat` rather than `each seat` with a helper inside.

A pair loop with no `sqrt`, 30 × 30, fits.
