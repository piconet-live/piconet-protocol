# Handbook: the online service's messages

Every message of the three links: launcher and coordinator (JSON over `/ws`), relay and coordinator (JSON over `/relay`), game and relay
(UDP). The types are in `net/src/Online.Protocol/`.

"C" is the coordinator, "L" a launcher, "R" a relay, "G" a game. The name is the JSON `type` (or the UDP type number).
A seat is a racer's place in a race, 0 to 29. A match is a lobby's next race from its forming to its start.

| Message | Who to whom | When | What the receiver does |
|---|---|---|---|
| `hello` | L to C | first message | Checks version, key, name, build, line; refuses with `error` (`bad_version`, `bad_message`, `bad_name`, `bad_build`, `bad_line`, `tags_taken`); a `password` field (launchers before custom lobbies) is passed over or answers `welcome` (player id, server name, rules, `tag`). The rules (`ServerRules`) are the lobby's as the launcher shows them, among them (0.5.0) `latencyLimitMs`, the lobby's latency limit (`HostRules.LatencyLimit`: 30 to 300 ms, Quick Play 250); a launcher from before passes over it, and a rules message without it reads as 250. Optional `key`, `players` (players list), `line` (the release line, below) and `modes` (the newest game-mode api the launcher runs: a player is put in a mode's race only with that api or newer), `avatar` (the picture the player chose to stand for them, by name: below the table) One a connection: a second is `already_welcomed`. Test settings, the key, limits: below the table |
| `queue` | L to C | RACE pressed (machine, colour, setting) | Puts the player in a lobby; `error` `machine_not_allowed`, or `not_ready` when already in one |
| `leaveQueue` | L to C | the player leaves Quick Play | Takes them out of the lobby and any match; answers `leftLobby` |
| `browseLobbies`, `lobbyList` | L to C, C to L | the lobby browser opens (`on` true) or closes | The custom lobbies of the player's build (`LobbySummary`: id, name, host's name, players, capacity, `locked`, rules whole, stage), on subscribe and then on changes, at most about twice a second; `quickPlay`: the server's own rules in the same form, to compare with; `modes`: the server's official modes a host may give a lobby (`LobbyMode`, its catalog's lobby modes in order, settings on their defaults), the create screen's list. Also sent once after `welcome` to a hello that says `modes`, and again when Quick Play's rules change, with `modes` left out: Quick Play's rules outside the browser (below the table) |
| `createLobby`, `joinLobby` | L to C | the player makes or joins a custom lobby | Name, rules (`LobbyRules`: the mode, one of `lobbyList.modes` by its hash in `mode` or the host's own `.fzmode` file in `modeFile`, with its settings in `modeValues`; beside it only the lobby's own settings, `LobbyRules.LobbyKeys` (seats, ping limit, vote and choice times, voting minimum, how the course is found among the mode's), and, when the mode's stats choice is the host's file, the host's own `.fzstats` file whole in `packFile`, at most 256K characters, with its name in `statPack`. How the race plays is the mode's: a rule of the race in `values` (the CPUs and teams too) or a server pack by name alone is `bad_rules` with one line, `OutsideTheMode`; a lobby with no mode races Quick Play's normal mode; the server sends a lobby's rules whole, with the pack by name), optional password, and the racer as `queue` names one; the creator is host. Leaves the lobby the player was in; `error` `no_such_lobby`, `bad_lobby_password`, `lobby_full`, `lobby_closed` (starting or choosing, or the player was kicked from it), `bad_lobby_name`, `bad_rules` |
| `setLobbyRules`, `kick`, `startLobbyRace` | L to C | the host's controls | While the lobby is forming: name, rules whole, password (or `clearPassword`); a member out (`leftLobby` reason `kicked`, no rejoin); the race now. `error` `not_host`, `rules_locked`, `bad_rules`. Numbers outside a rule's range are brought into it (`HostRules`) |
| `leaveLobby` | L to C | the player leaves any lobby | As `leaveQueue` |
| `voteStart`, `voteCpuFill` | L to C | lobby buttons | Counts the vote; `error` `voting_closed` or `not_in_lobby`; a start vote of more than half begins the match, before a Quick Play lobby's start countdown runs out |
| `avatar` | L to C | the player's avatar changed while connected (`welcome` says `avatars`) | Checked as the hello's `avatar`; none for null, empty or no picture's name; the picture asked for once if lacking, the lobby sent again (below the table, "Avatars") |
| `name` | L to C | a name change while connected (`welcome` says `renames`) | Checked as a hello's name, logged (`id: old -> new`), the lobby sent again; `error` `bad_name`, or `rename_too_soon` within a second of the last (the launcher sends its saved name again) |
| `chat` | L to C, C to L | any time after hello | Sends it to everyone (`global`) or the lobby (`lobby`); `error` `bad_text`, `bad_channel`, or the filter's `chat_*` (the service's chat filter). A player's line has `fromTag`; a notice is a `chat` from id `server`, name `SYSTEM`, with `level`, `noticeId` and no tag |
| `recentNotices` | C to L | after `welcome`; joining a lobby | The last hour's notices to all, or that lobby, if hello said `notices`; listed once by id, never as chat |
| `lobby` | C to L | every change of the lobby | The launcher shows it: members (each with `tag`, and `custom`, the garage-built machine they race, absent for a stock machine; a launcher from before it passes over it), stage, votes, the race, `waiting`, `otherBuilds` (one entry per release line or build; an entry of a line has `line`, and its `build` is the build most of its players have, for a launcher from before lines); `startsInMs` (0.5.2): a Quick Play lobby's start countdown, the milliseconds left when sent (`HostRules.StartCountdown`: from the voting minimum, shorter with each player, down to 10 s; a full lobby starts at once); absent: none runs, and a launcher from before passes over it |
| `tag` | C to L | a rename changed the tag (worked out again from the key; without a key, the pair was another player's) | The new tag (`TagChanged`), for this connection: the launcher uses it and saves nothing. A launcher of 0.2.x drops it as an unknown type |
| `rules` | C to L | the operator changed a live rule that a welcome's rules carry (`Coordinator.TellRules`) | The rules whole (`RulesChanged`), to every welcomed launcher, in a lobby or not; the launcher shows them at once (lobby, home). A running race keeps its match start's set-up; the next match start has the new rules. A 0.2.x launcher drops it as an unknown type |
| `players` | C to L | after `welcome`, if hello said `players` | The whole players list (`PlayersList`) as it last went out, in parts (`part`, `parts`) of at most 50: id, name, tag, colour, `status` (`online`, `lobby`, `racing`, `watching`) and `lobby` |
| `playersChanged` | C to L | at most every 500 ms, when something changed | `joined`, `changed` (whole entries), `left` (ids), 50 entries at most a message; the rest go in the next |
| `measureRelays` | C to L | a match forms; again for a relay started for it | The launcher pings each relay over UDP and answers `relayLatency` within 3 s |
| `relayLatency` | L to C | the answer | Stored per player and relay for the choice of region (section 5) |
| `courseVote`, `voteCourse` | C to L, L to C | with `measureRelays`, when the server's course mode is "vote" | Options and duration out (with Quick Play's rotation, a card an option: its mode, laps, its mode's line, a spicy mode's badge and rule lines, and the card's rules for the vote panel: below the table); an option index back (it may be changed until the vote ends) |
| `courseChosen` | C to L | the vote ended | Votes per option, the option drawn, the course: the launcher's draw animation |
| `chooseMachine`, `machineChoice` | C to L, L to C | after the course is decided | Course, allowed machines, duration out; the racer's machine back, kept from then on |
| `statPack` | C to L | after `welcome`, to a launcher whose hello said `statPacks`, on a server with a stat pack | The pack whole, for the launcher's Garage (`handbook/stat-packs.md`); a race's pack is its `matchStart`'s |
| `matchStart` | C to L | machines chosen and the relay ready | Relay address and port, token (16 hex digits), seat, the whole race set-up, `reportLive`; the launcher writes a match file and starts the game. Since 0.4.0 the set-up has `grid`: the seats in starting order, pole first (below the table, "The start grid") |
| `requeued` | C to L | the player is out of this match | Reasons `latency` (the player's round trip to the race's relay is above the lobby's latency limit, or they did not answer), `no_relay`, `relay_failed`; the player stays in the lobby and watches that race |
| `quickPlayEnded` | C to L | every relay is full; or the coordinator is retired (any hello); or a hello over its address's limit | Reason `at_capacity`, or a retired coordinator's few words with `message`, its whole text; the player is out of Quick Play and the launcher goes to Home with the notice |
| `raceReport`, `liveRace` | L to C, C to L | during a race, at most 10 a second | A snapshot of all racers from one racer's game; passed on to the lobby's members who are not racing |
| `raceOver` | L to C | the player's game closed | The player is back in the lobby. Optional `final` (0.4.0): the race as the game last showed it (a `raceReport`'s snapshot), from which the coordinator takes the lobby's last finish |
| `raceDesynced` | C to L | a relay disconnected this player's seat | The launcher stops the game and says why |
| `watch`, `watchStart` | L to C, C to L | the player chooses Watch in the lobby (a welcome with `spectators`); each race of the lobby | `watch` yes or no: a watcher is not present for votes or the next match. `watchStart`: relay, a spectator's token, the seat watched first and the set-up; the launcher starts the game with `"spectate": true` |
| `preview`, `previewStart` | L to C, C to L | a player in no lobby previews a lobby's race (the Quick Play page's live card; a welcome with `previews`); each race of that lobby until stopped | `preview` lobby id, yes or no, at most one a second. A previewer is no member: not in the roster, counts, votes, grid or chat; at most 16 a race. `previewStart`: `watchStart`'s fields and the lobby id; one token per player and race. Refused: `no_such_lobby` (not listed to the player: another line or build, a password, no members), `in_lobby`, `rate_limited` (the connection stays), `no_spectators`. Ends with yes false, entering any lobby or Quick Play, or leaving |
| `ping`, `pong`, `pings` | C/L | hello says `pings` | Lobby pings (`Pings.cs`); the relay's is the launcher's own claim: display only, 0 to 999 |
| `hello`, `welcome`, `refused` | R to C, C to R | the relay connects to `/relay` | Id, region, public host and port, both versions, the key, and (optional: an older relay sends none) `takes`: the later messages it understands (`endRace`, `endSeat`, `spectators`, `previews`); refused with `bad_key`, `bad_version`, `bad_message` |
| `createRace` | C to R | a race starts | Match id and one token per player seat; the relay creates the race |
| `status` | R to C | every 5 s | Races, players, match ids; a relay silent for 15 s is dropped. Optional, passed over by an older end: `details`, `incidents`, `cpu`, `bytesOut` (`RelayControl.cs`). At most 48 KB, the rest left out (`RelayHost.StatusText`) |
| `endRace` | C to R | the operator ends a race (dashboard, `admin.sh end-race`) | Only to a relay whose hello said `takes: ["endRace"]`: a type an end does not know ends its link. The race ends as at its time limit |
| `endSeat` | C to R | the operator kicks or bans a player in a race | Only to a relay whose hello said `takes` it. Match id and race seat: the seat goes to the AI at the next tick as a silent seat does ("AI drives", every game alike; incident `removed`) |
| `addSpectator` | C to R | a lobby member watches a race, or a player outside it previews it | Only to a relay whose hello said `takes` it. Match id and a spectator's token (hex); `preview: true` (optional) for a previewer, sent only to a relay whose hello said `previews`, which counts them apart: at most 32 members a race, and the previews the coordinator allows (`--max-previewers`, at most 500) |
| `seatDesynced` | R to C | a seat was disconnected for a desync | The coordinator sends that player `raceDesynced` and puts them back in the lobby |
| Ping 1, Pong 2 | anyone to R, R back | measuring; during a race | Pong repeats the ping; for a seat of a running race it adds the race clock in ms, which paces the game. A game's ping ends with its token, and its pong carries that seat's key (a launcher's: none, key 0) |
| Join 3, Joined 4 | G to R, R to G | the game starts (resent until answered); again after a second with no official frame | Token in; session, seat, the mask of player seats, the **cookie** of the Join's address, flags (1: returning, catching up; 4: a spectator, below) and the frames closed out. A Join never makes its address the seat's |
| Ready 5, Go 6 | G to R, R to G | the game is at the start gate (resent); every seat is ready | Token and cookie; Go carries the start barrier's frame; resent every 100 ms until the game's first input |
| Input 7 | G to R | every step (resent until official frames show it) | Token, cookie, first frame, the next official frame the game lacks, one six-byte control record per frame (none: an acknowledgement, while the AI drives the seat) |
| Official 8 | R to G | every closed frame; resent every 50 ms until acknowledged | For each frame the records of all player seats in seat order |
| Checksum 9 | G to R | the game reports every 60th step | Token, cookie, frame, 32-bit value; compared when every reporting seat has sent that frame |
| Desync 10, SeatLeft 11 | R to G | a seat is out of sync; a seat left | Desync goes to the seat concerned; SeatLeft to all: that seat's input is "AI drives" from now on |
| SeatBack 12 | R to G | a returning game has run the race to within 60 frames (its checksum reports) | Seat, frame H (240 frames on): the seat's records are "AI drives" below H and its player's from H; to all, and to that game every 100 ms until H closes |
| Over 13 | G to R | the game's race-over test said "over" on official steps (a few times) | Token, cookie, step: no seat comes back from here |

A spectator's token (stage A2, `Relay/RaceSession.Watchers.cs`) is sent the race from its acknowledgement like a
returning seat, then the frames as they close; its input records are dropped; it never counts for the go, the
barrier, early closing or the race's end; its checksums are held against the racers' agreed value and a difference
is only logged ("spectator N's game differs"), with a line per race at its end.

Ready, Input, Checksum and Over are dropped unless their cookie fits their own source address
(`Inbound/RelayCookies.cs`, `RelayServer.Handle`): the race goes only to an address that proved it receives there.

Every packet from R to G (Pong, Joined, Go, Official, Desync, SeatLeft, SeatBack) opens with the **key** of the
seat it goes to, 8 bytes after the header: SHA-1 of "fzgx relay key" and the seat's token, its first 8 bytes
(`Online.Protocol/RelayKeys.cs`; `RelayServer` stamps each packet as it sends it, `RaceSession.Keys.cs`). The game
drops a packet from the relay's address whose key is not its own (`port/core/src/relay_wire.cpp`, `Handle`), so only
the relay, which holds the token, can stop a game or feed it frames (before the key, anyone who
could send from the relay's address could). The fields in the table above follow the key.

**The start grid** (`Matchmaking/StartGrid.cs`: the Grand Prix's order, the reverse of the
last race's finish, with the players always ahead of the computer racers). Every match start's `setup.grid` lists every seat once, pole
first: the players ranked by the lobby's last race in reverse of its finish (its last on pole, its winner last among
them), then the players it did not rank (a lobby's first race, a player who joined since) in an order drawn from the
set-up's `shuffleSeed` (Fisher-Yates on xorshift32), then the CPU seats in seat order; a player who left is not in
the race. The last finish is the standings (`RaceSnapshot.Standings`: finished by time, then racing by distance, then
knocked out, who counts as ranked, last) of the latest snapshot of the lobby's race the coordinator was given: the
live reports and every racer's `raceOver.final`, the highest frame kept; it is taken when the lobby forms again. A
watcher's `watchStart` carries the same set-up. The game places the grid as the list says (`port/image/sites_grid.py`);
without `grid` (a set-up from before 0.4.0, every replay of one) the game sorts its own: the CPUs ahead of seat 0,
which starts last. Logged at each start: `match ID: start grid (pole first) ...; K of its N players ranked by the lobby's last race`.

**Test settings in `hello`**: a launcher whose launch file's `debug` part sets something that reaches a race
(`Launcher.Core` `DebugSettings.ReachesARace`) says `"debug": true` (an optional field), and is refused with
`debug_not_allowed`, "this server does not take games with test settings on", unless the server was started with
`--allow-debug` (`Coordinator.OnHello`). It is a guard against an accident (a tester racing with
simulated lag left on), not a protection. The official service refuses: `deploy/fly/coordinator.toml` does not pass
the option (`DebugRefusalTests` fails if it does), so a test visit to it (the manual gate
`service`) is refused. `quickplay-run.sh`'s own test server passes it; `pair-run.sh` and `match-file.sh` need none
(their players are bots).

**The line in `hello`** (`Hello.Line`, a launcher of 0.5.2 on): the launcher's release line, the first two numbers
of its version (`VersionLine.Of`: 0.5.2 says `"0.5"`; a launcher built with no version, 0.0.x, or a pre-release
says none). Every build of a line races together (the owner's rule): the coordinator keys a player's lobby to the line
when there is one, else to the build as before (`handbook/service.md` in the service, "What identifies a build").
Written only when set, so a server from before it passes over it and the version stayed 3. A text that is not two
numbers of one to four digits, no leading zero, and not `0.0` (`VersionLine.IsLine`) is refused with `bad_line`
(`Hello.BadLine`). Like the build it is a claim the server cannot check; that the builds of a line compute the same
race is the release's to show (the gate row `mixed`). The status page shows each lobby's `line`, and the released
builds that name none with the line the server gives them (`lineBuilds`, `lineBuildsFault`).

**The message of the day in the status** (`ServiceStatus.Motd`): while the operator has one set, GET /api/status
carries it as one `RecentNotice` (`id`, `level`, `text` without the tier's word, `time`), the same notice the
coordinator sends as a chat line at each welcome. A launcher on its home screen says no hello, so the status it asks
every few seconds is how it sees the message there, and it goes when the field does. Written only when set: a reader
from before passes over it.

**The Quick Play page's timing and mode in the status** (2026-11-19; the native program's Quick Play page,
`lobby-screens-data.md` proposal items 1 and 2): `ServiceStatus.Timers` (`StatusTimers`: the course vote, the draw
shown, the machine choice, the straggler wait and the start countdown at its longest, in ms); each lobby's
`startsInMs` (while its start countdown runs: an upper bound, a joiner or a start vote brings it nearer) and
`votingMinimum`; each match on its way's `lobbyId` and `endsInMs` (the time left on its stage's clock: the vote, the
draw shown, the machine choice, which can end early; null for a stage with no clock); each race's `mode`, `badge` and
`pack` (its stat pack's name; null: none). All optional: a reader from before passes over them, and a server from
before sends none, so a screen shows the shorter line, never a number (tests: Online.Tests `StatusTimingTests`).

**The lobby screens' last facts** (2026-11-20; `lobby-screens-data.md` proposal items 3, 4, 6 and 7, and the
avatars). All optional: a reader from before passes over them (the generated C++ reader and System.Text.Json both skip
a member they do not know), and a server from before sends none, so a screen shows the shorter line, never a number
(tests: Online.Tests `LobbyFactsTests`, Protocol.Tests `QuickPlayRotationTests.ACardSaysItsRulesAndWhichAreNotQuickPlays`).

- *A lobby's relay, for the ping.* Each status lobby's `relay`: the relay id of its race while it races, or of its match
  once that match's relay is chosen (after its players' measures, at the vote's end); absent while it has none. Each
  status relay's `host` and `port`, while it is connected: where a game sends the relay protocol's keyless Ping 1 to
  time its own round trip, the same endpoint a match's `measureRelays` names (an empty host: the coordinator's own, as
  there). Already given to every player who queues; the status now shows it to anyone who reads it.
- *Quick Play's rules outside the browser* (the Quick Play page, a Quick Play lobby's RULES line): `lobbyList` once
  after `welcome` (a hello that says `modes`) and again to every such game when the operator's rules or the mode
  store's set change what Quick Play's rules say (a browser has it with its next list). Its `quickPlay` names Quick
  Play's normal mode on its defaults in `mode` (`terms`, `pack`, `params`, `values`: what a Quick Play race's rules
  are); its `values` stay the server's options, as a custom lobby is compared with them.
- *A running race's end.* Each status race's `endsInMs`: about how long until the race is over as the game ends it,
  from the status's `time`, an estimate from its latest live report (at most 10 s old) and the server's clock. Each
  racer still racing finishes after its laps left at its own mean lap so far (the current lap's time counted off); the
  race ends at the last of those, and with a finish limit no later than the limit after the first finish; once a racer
  has finished the limit is counted from that finish's race time (the race's clock is the racers' still racing), the
  one exact part. 0: no racer still racing. Absent: no live report (or one too old), a racer still racing with no whole
  lap and no finish yet, laps unlimited, or a mode that ends its race otherwise (`end_race` in its code, `clock_limit`,
  `race_waits everyone`: Knockout, Overdrive, Death Race and Crown; Quick Play's normal mode and Ghost race end by
  their laps). The results screens and the lobby's own waits (`timers`) come after it.
- *A vote card's rules* (`VoteCard`): `about` on every card whose mode has a line (a launcher from before shows it
  only beside a badge); `terms`, the race's rules on the card by the custom rules' keys (`laps` on its course, or on
  Random the mode's one number or -1 for laps by course; `racers`, the race's seats; `cpu-fill` 0 vote, 1 always, 2
  never; `cpu-level`; `boost-start`; `mine-regen` in ms, 0 none; `finish-limit` in s, 0 none; `machine-setting` and
  `machine-setting-high`; `mixed-packs`; `course-select` with `course-select-course` only for a mode that pins its
  course); `pack`, the stat pack by name ("" the disc's own); `moved`, the keys (and `stat-pack` for the pack) whose
  value is not Quick Play's own (its normal mode on its defaults on the same course, with Quick Play's seats): empty, the
  card races Quick Play's rules; absent, the server has no rotation to compare with. A rotation that shows no laps
  leaves `laps` out of both. The owner's display rule (2026-11-20): the essentials are always shown, the stat pack, the
  mode, `laps`, `racers` and `cpu-fill`; every other rule line only when its key is in `moved`.
- *Avatars.* The hello's `avatar`: a picture's name (64 lower-case hex digits, the SHA-256 of the game's 64 x 64
  picture, as an emblem slot names one), the player's own choice. The server checks it as an emblem's name (anything
  else is dropped, as no avatar; the hello stands), keeps it for the connection, asks for its picture once when it
  lacks it (`emblemsWanted`, answered by `emblemPicture`), hands the picture to any game that asks by name
  (`emblemAsk`), and names it in an emblem report of that player. It is carried by name in a lobby's `players`
  (`PlayerInfo.avatar`) and in the status's racers (`RacerView.avatar`, the lobbies' and the races'); absent: none
  chosen. The hide list applies by the racer's tag and name, a report by the player's id (a lobby's roster, or the
  players list for a status row). Changed while connected, in a lobby or not: the client message `avatar` (`SetAvatar`),
  `avatar` the new name, checked as the hello's; null, empty or no picture's name: none from now on. The server asks
  for a picture it lacks once (`emblemsWanted`), sends the player's lobby again and shows it in the status from then
  on (a race running keeps the racers it started with); the same name again changes nothing. Sent only to a server
  whose `welcome` says `avatars: true`: a server from before answers the unknown type with `error` `bad_message`
  ("unreadable message") and keeps the connection, and passes over a hello's `avatar` silently.

**The official modes in the status****The official modes in the status** (`ServiceStatus.ModeStore`, `ModeStoreView`): where the coordinator loaded Quick
Play's rotation from (`source`: `bucket`, the stack's mode store; `local copy`, its last good copy while the bucket
cannot be reached; `image seed`, the image's quickplay folder while neither is there; `image`, a server with no store),
a `fault` line while it is not the bucket's, and the rotation's modes (`modes`: `file`, `name`,
the package's `hash`, `role` normal, spicy or lobby, a spicy one's `weight`). The operator and the deploy tooling read
it; no launcher does. Written only when the server has a rotation: a reader from before passes over it.

**The key in `hello`** (`Hello.Key`): the launcher's secret, 32 random bytes in base64url without padding (43
characters, `PlayerTags.NewKey`), made once and kept in its settings (`LauncherSettings.PlayerKey`). The coordinator
works the player's tag out from it with its own tag key (a launcher cannot
work out its tag, so cannot search for a secret that gives a chosen one), keeps it for the connection only and
never logs it or sends it to anyone. A key of any other form (another spelling of the same bytes included) is
refused with `bad_message`; one that is no JSON string makes the hello unreadable, also `bad_message`. No key (a
launcher of 0.2.x): a random tag. A secret only ever gets its own tags (numbers 0 to 4,095); with every one of
them held for that name, the hello is refused `tags_taken` (`Hello.TagsTaken`). No hello chooses a tag: a `tag` field, sent by no released launcher, is passed
over as any field a message does not have.

**Too many hellos**: from one address (IPv6: one /48) more than 40 hellos at once, or 20 a minute after, are answered `quickPlayEnded`
(reason "too many joins", with its words) and the connection is closed.
Every launcher shows it as a retired coordinator's words and goes home.
