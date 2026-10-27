# Handbook: the online service's messages

Every message of the three links: launcher and coordinator (JSON over `/ws`), relay and coordinator (JSON over `/relay`), game and relay
(UDP). The types are in `net/src/Online.Protocol/`.

"C" is the coordinator, "L" a launcher, "R" a relay, "G" a game. The name is the JSON `type` (or the UDP type number).
A seat is a racer's place in a race, 0 to 29. A match is a lobby's next race from its forming to its start.

| Message | Who to whom | When | What the receiver does |
|---|---|---|---|
| `hello` | L to C | first message | Checks version, key, name, build; refuses with `error` (`bad_version`, `bad_message`, `bad_name`, `bad_build`, `tags_taken`); a `password` field (launchers before custom lobbies) is passed over or answers `welcome` (player id, server name, rules, `tag`). The rules (`ServerRules`) are the lobby's as the launcher shows them, among them (0.5.0) `latencyLimitMs`, the lobby's latency limit (`HostRules.LatencyLimit`: 30 to 300 ms, Quick Play 250); a launcher from before passes over it, and a rules message without it reads as 250. Optional `key` and `players` (players list) One a connection: a second is `already_welcomed`. Test settings, the key, limits: below the table |
| `queue` | L to C | RACE pressed (machine, colour, setting) | Puts the player in a lobby; `error` `machine_not_allowed`, or `not_ready` when already in one |
| `leaveQueue` | L to C | the player leaves Quick Play | Takes them out of the lobby and any match; answers `leftLobby` |
| `browseLobbies`, `lobbyList` | L to C, C to L | the lobby browser opens (`on` true) or closes | The custom lobbies of the player's build (`LobbySummary`: id, name, host's name, players, capacity, `locked`, rules whole, stage), on subscribe and then on changes, at most about twice a second; `quickPlay`: the server's own rules in the same form, to compare with |
| `createLobby`, `joinLobby` | L to C | the player makes or joins a custom lobby | Name, rules (`LobbyRules`), optional password, and the racer as `queue` names one; the creator is host. Leaves the lobby the player was in; `error` `no_such_lobby`, `bad_lobby_password`, `lobby_full`, `lobby_closed` (starting or choosing, or the player was kicked from it), `bad_lobby_name`, `bad_rules` |
| `setLobbyRules`, `kick`, `startLobbyRace` | L to C | the host's controls | While the lobby is forming: name, rules whole, password (or `clearPassword`); a member out (`leftLobby` reason `kicked`, no rejoin); the race now. `error` `not_host`, `rules_locked`, `bad_rules`. Numbers outside a rule's range are brought into it (`HostRules`) |
| `leaveLobby` | L to C | the player leaves any lobby | As `leaveQueue` |
| `voteStart`, `voteCpuFill` | L to C | lobby buttons | Counts the vote; `error` `voting_closed` or `not_in_lobby`; a start vote of more than half begins the match, before a Quick Play lobby's start countdown runs out |
| `name` | L to C | a name change while connected (`welcome` says `renames`) | Checked as a hello's name, logged (`id: old -> new`), the lobby sent again; `error` `bad_name`, or `rename_too_soon` within a second of the last (the launcher sends its saved name again) |
| `chat` | L to C, C to L | any time after hello | Sends it to everyone (`global`) or the lobby (`lobby`); `error` `bad_text`, `bad_channel`, or the filter's `chat_*` (the service's chat filter). A player's line has `fromTag`; a notice is a `chat` from id `server`, name `SYSTEM`, with `level`, `noticeId` and no tag |
| `recentNotices` | C to L | after `welcome`; joining a lobby | The last hour's notices to all, or that lobby, if hello said `notices`; listed once by id, never as chat |
| `lobby` | C to L | every change of the lobby | The launcher shows it: members (each with `tag`), stage, votes, the race, `waiting`, `otherBuilds`; `startsInMs` (0.5.2): a Quick Play lobby's start countdown, the milliseconds left when sent (`HostRules.StartCountdown`: from the voting minimum, shorter with each player, down to 10 s; a full lobby starts at once); absent: none runs, and a launcher from before passes over it |
| `tag` | C to L | a rename changed the tag (worked out again from the key; without a key, the pair was another player's) | The new tag (`TagChanged`), for this connection: the launcher uses it and saves nothing. A launcher of 0.2.x drops it as an unknown type |
| `rules` | C to L | the operator changed a live rule that a welcome's rules carry (`Coordinator.TellRules`) | The rules whole (`RulesChanged`), to every welcomed launcher, in a lobby or not; the launcher shows them at once (lobby, home). A running race keeps its match start's set-up; the next match start has the new rules. A 0.2.x launcher drops it as an unknown type |
| `players` | C to L | after `welcome`, if hello said `players` | The whole players list (`PlayersList`) as it last went out, in parts (`part`, `parts`) of at most 50: id, name, tag, colour, `status` (`online`, `lobby`, `racing`, `watching`) and `lobby` |
| `playersChanged` | C to L | at most every 500 ms, when something changed | `joined`, `changed` (whole entries), `left` (ids), 50 entries at most a message; the rest go in the next |
| `measureRelays` | C to L | a match forms; again for a relay started for it | The launcher pings each relay over UDP and answers `relayLatency` within 3 s |
| `relayLatency` | L to C | the answer | Stored per player and relay for the choice of region (section 5) |
| `courseVote`, `voteCourse` | C to L, L to C | with `measureRelays`, when the server's course mode is "vote" | Options and duration out; an option index back (it may be changed until the vote ends) |
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
| `ping`, `pong`, `pings` | C/L | hello says `pings` | Lobby pings (`Pings.cs`); the relay's is the launcher's own claim: display only, 0 to 999 |
| `hello`, `welcome`, `refused` | R to C, C to R | the relay connects to `/relay` | Id, region, public host and port, both versions, the key, and (optional: an older relay sends none) `takes`: the later messages it understands (`endRace`, `endSeat`, `spectators`); refused with `bad_key`, `bad_version`, `bad_message` |
| `createRace` | C to R | a race starts | Match id and one token per player seat; the relay creates the race |
| `status` | R to C | every 5 s | Races, players, match ids; a relay silent for 15 s is dropped. Optional, passed over by an older end: `details`, `incidents`, `cpu`, `bytesOut` (`RelayControl.cs`). At most 48 KB, the rest left out (`RelayHost.StatusText`) |
| `endRace` | C to R | the operator ends a race (dashboard, `admin.sh end-race`) | Only to a relay whose hello said `takes: ["endRace"]`: a type an end does not know ends its link. The race ends as at its time limit |
| `endSeat` | C to R | the operator kicks or bans a player in a race | Only to a relay whose hello said `takes` it. Match id and race seat: the seat goes to the AI at the next tick as a silent seat does ("AI drives", every game alike; incident `removed`) |
| `addSpectator` | C to R | a lobby member watches a race | Only to a relay whose hello said `takes` it. Match id and a spectator's token (hex); at most 32 a race |
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
