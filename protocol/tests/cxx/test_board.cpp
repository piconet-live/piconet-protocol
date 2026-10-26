// The status board's generated C++ header (protocol/generated/status_board.g.h): its header's making and taking,
// each block's round trip, the ring's overwrite-oldest, and torn reads under stress, by two threads and by two
// processes sharing a file mapping. With --write FILE it writes the known board the C# test and test_board.py read;
// with --read FILE it checks the one the C# test wrote (BoardCheck --write); with --serve FILE it is the game for
// the C# test's reader in another process: it takes the board the C# test made and writes it for 1.5 s. Linux.
#include "status_board.g.h"

#include <sys/mman.h>
#include <sys/wait.h>
#include <fcntl.h>
#include <unistd.h>

#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <thread>
#include <vector>

namespace b = fzgx::board;

namespace
{
int s_failed = 0;

#define CHECK(x)                                                              \
  do                                                                          \
  {                                                                           \
    if (!(x))                                                                 \
    {                                                                         \
      std::fprintf(stderr, "%s:%d: CHECK failed: %s\n", __FILE__, __LINE__, #x); \
      ++s_failed;                                                             \
    }                                                                         \
  } while (0)

struct Memory
{
  alignas(64) unsigned char bytes[b::SIZE] = {};
};

// Every field of a snapshot from one number: a torn read shows as two numbers in one read.
b::Snapshot SnapshotOf(std::uint32_t k)
{
  b::Snapshot s;
  s.serial = k;
  s.frame = k * 3;
  s.local_seat = k % 30;
  s.count = 30;
  for (std::uint32_t i = 0; i < 30; ++i)
  {
    b::Racer& r = s.racers[i];
    r.seat = static_cast<std::uint8_t>(i);
    r.state = static_cast<b::RacerState>(k % 3);
    r.rank = static_cast<std::uint8_t>(k + i);
    r.flags = b::RACER_FLAGS_HAS_BEST_LAP;
    r.lap = static_cast<std::uint16_t>(k);
    r.kos = static_cast<std::uint16_t>(k + 1);
    r.max_energy = static_cast<std::uint16_t>(k + 2);
    r.lap_ms = k + i;
    r.total_ms = k * 2 + i;
    r.best_lap_ms = k * 5;
    r.speed = static_cast<float>(k) + 0.5f;
    r.energy = static_cast<float>(k) * 2;
    r.top_speed = static_cast<float>(k) * 3;
    r.x = static_cast<float>(k) - 1;
    r.y = static_cast<float>(i);
    r.z = -static_cast<float>(k);
    r.distance = static_cast<float>(k) * 10;
  }
  return s;
}

bool Same(const b::Snapshot& a, const b::Snapshot& c)
{
  if (a.serial != c.serial || a.frame != c.frame || a.local_seat != c.local_seat || a.count != c.count)
    return false;
  for (int i = 0; i < 30; ++i)
  {
    const b::Racer &x = a.racers[i], &y = c.racers[i];
    if (x.seat != y.seat || x.state != y.state || x.rank != y.rank || x.flags != y.flags || x.lap != y.lap ||
        x.kos != y.kos || x.max_energy != y.max_energy || x.lap_ms != y.lap_ms || x.total_ms != y.total_ms ||
        x.best_lap_ms != y.best_lap_ms || x.speed != y.speed || x.energy != y.energy || x.top_speed != y.top_speed ||
        x.x != y.x || x.y != y.y || x.z != y.z || x.distance != y.distance)
      return false;
  }
  return true;
}

b::Event EventOf(std::uint64_t n)
{
  b::Event e;
  e.kind = static_cast<b::EventKind>(1 + n % 6);
  e.beat = static_cast<std::uint32_t>(n * 7);
  e.step = static_cast<std::uint32_t>(n * 11);
  e.a = static_cast<std::uint32_t>(n);
  e.b = static_cast<std::uint32_t>(~n);
  e.ms = static_cast<std::uint32_t>(n * 13);
  return e;
}

bool SameEvent(const b::Event& e, std::uint64_t n)
{
  const b::Event x = EventOf(n);
  return e.seq == static_cast<std::uint32_t>(n * 2 + 2) && e.kind == x.kind && e.beat == x.beat && e.step == x.step &&
         e.a == x.a && e.b == x.b && e.ms == x.ms;
}

void Header()
{
  Memory m;
  b::View v(m.bytes);
  CHECK(v.Take(77, b::SIZE) == b::View::Taken::NotABoard);
  CHECK(v.HeaderRunnerVersion() == 0);  // (nothing written into what is no board)
  v.Make(42);
  CHECK(std::memcmp(m.bytes, "FZSB", 4) == 0);
  CHECK(v.HeaderVersion() == b::VERSION && v.HeaderSize() == b::SIZE && v.HeaderLauncherPid() == 42);
  CHECK(v.Take(77, b::SIZE) == b::View::Taken::Ok);
  CHECK(v.HeaderRunnerVersion() == b::VERSION && v.HeaderRunnerSize() == b::SIZE && v.HeaderRunnerPid() == 77);
  // a launcher of another version: refused, and the game's own version is there to say so
  Memory other;
  b::View o(other.bytes);
  o.Make(1);
  o.SetHeaderVersion(b::VERSION + 1);
  CHECK(o.Take(5, b::SIZE) == b::View::Taken::OtherVersion);
  CHECK(o.HeaderRunnerVersion() == b::VERSION && o.HeaderRunnerPid() == 5);
  // too small: by the launcher's word, or by the mapping
  Memory small;
  b::View s(small.bytes);
  s.Make(1);
  s.SetHeaderSize(b::SIZE - 64);
  CHECK(s.Take(5, b::SIZE) == b::View::Taken::TooSmall);
  s.SetHeaderSize(b::SIZE);
  CHECK(s.Take(5, b::SIZE - 1) == b::View::Taken::TooSmall);
  CHECK(s.Take(5, 16) == b::View::Taken::NotABoard);
  // the header is where version 1 put it, whatever the version
  static_assert(b::at::HEADER_MAGIC == 0 && b::at::HEADER_VERSION == 4 && b::at::HEADER_SIZE == 8 &&
                b::at::HEADER_LAUNCHER_PID == 12 && b::at::HEADER_RUNNER_VERSION == 16 &&
                b::at::HEADER_RUNNER_SIZE == 20 && b::at::HEADER_RUNNER_PID == 24);
  static_assert(b::at::PULSE % 64 == 0 && b::at::STATUS % 64 == 0 && b::at::SNAPSHOT % 64 == 0 &&
                b::at::EVENTS % 64 == 0 && b::at::MAILBOX % 64 == 0 && b::SIZE % 64 == 0);
  static_assert(b::at::MAILBOX + 4 + 12 + 128 <= b::SIZE);
}

void RoundTrips()
{
  Memory m;
  b::View v(m.bytes);
  b::Status st;
  CHECK(v.ReadStatus(&st) && st.phase == b::Phase::None);  // a board of zeros reads as nothing yet
  st.phase = b::Phase::Racing;
  st.flags = b::STATUS_FLAGS_WAITS_FOR_RELAY | b::STATUS_FLAGS_REJOIN;
  st.step = 1234;
  st.stop_seen = 2;
  st.notice_seen = 3;
  v.WriteStatus(st);
  b::Status back;
  CHECK(v.ReadStatus(&back) && back.phase == b::Phase::Racing && back.flags == 9 && back.step == 1234 &&
        back.stop_seen == 2 && back.notice_seen == 3);
  v.WriteSnapshot(SnapshotOf(7));
  b::Snapshot snap;
  CHECK(v.ReadSnapshot(&snap) && Same(snap, SnapshotOf(7)));
  b::Mailbox mail;
  mail.stop_serial = 1;
  mail.notice_serial = 4;
  const char text[] = "Ready in 10 s";
  mail.notice_length = sizeof(text) - 1;
  std::memcpy(mail.notice_text.data(), text, mail.notice_length);
  v.WriteMailbox(mail);
  b::Mailbox got;
  CHECK(v.ReadMailbox(&got) && got.stop_serial == 1 && got.notice_serial == 4 && got.notice_length == 13 &&
        std::memcmp(got.notice_text.data(), text, 13) == 0 && got.notice_text[13] == 0);
  v.SetPulseHeartbeat(0x1'0000'0005ull);
  v.SetPulseStopReason(b::StopReason::Launcher);
  CHECK(v.PulseHeartbeat() == 0x1'0000'0005ull && v.PulseStopReason() == b::StopReason::Launcher);
  // a writer caught in the middle (the sequence odd): every try fails, and the reader says so
  std::atomic_ref<std::uint32_t>(*reinterpret_cast<std::uint32_t*>(m.bytes + b::at::STATUS)).fetch_add(1);
  CHECK(!v.ReadStatus(&back, 8));
}

void RingOverflow()
{
  Memory m;
  b::View v(m.bytes);
  b::Event e;
  CHECK(v.EventsHead() == 0 && v.ReadEvents(0, &e) == b::Read::NotYet);
  for (std::uint64_t n = 0; n < 200; ++n)
    v.WriteEvents(n, EventOf(n));
  CHECK(v.EventsHead() == 200);
  int lost = 0, ok = 0;
  for (std::uint64_t n = 0; n < 200; ++n)
  {
    const b::Read r = v.ReadEvents(n, &e);
    if (r == b::Read::Lost)
      ++lost;
    else if (r == b::Read::Ok && SameEvent(e, n))
      ++ok;
  }
  CHECK(lost == 200 - static_cast<int>(b::at::EVENTS_SLOTS) && ok == static_cast<int>(b::at::EVENTS_SLOTS));
  CHECK(v.ReadEvents(200, &e) == b::Read::NotYet);
  // two writers, numbers handed out between them: the head is the highest written, whatever the order
  v.WriteEvents(202, EventOf(202));
  v.WriteEvents(201, EventOf(201));
  CHECK(v.EventsHead() == 203);
  CHECK(v.ReadEvents(200, &e) == b::Read::NotYet && v.ReadEvents(201, &e) == b::Read::Ok && SameEvent(e, 201));
}

// One writer, one reader, in two threads or two processes: no read may mix two writes.
struct Tally
{
  long reads = 0, torn = 0, events = 0, bad_events = 0, lost = 0;
};

void Writer(b::View v, std::chrono::milliseconds how_long)
{
  const auto end = std::chrono::steady_clock::now() + how_long;
  std::uint32_t k = 1;
  std::uint64_t n = 0;
  while (std::chrono::steady_clock::now() < end)
  {
    v.WriteSnapshot(SnapshotOf(k));
    b::Status st;
    st.phase = static_cast<b::Phase>(k % 12);
    st.flags = k;
    st.step = k;
    st.stop_seen = k;
    st.notice_seen = k;
    v.WriteStatus(st);
    v.WriteEvents(n, EventOf(n));
    ++n;
    v.SetPulseHeartbeat(k);
    ++k;
  }
  v.SetPulseStopReason(b::StopReason::Game);  // (the reader's sign that the writer is done)
}

Tally Reader(b::View v)
{
  Tally t;
  std::uint64_t next = 0;
  while (v.PulseStopReason() != b::StopReason::Game)
  {
    b::Snapshot s;
    if (v.ReadSnapshot(&s, 1000))
    {
      ++t.reads;
      if (s.serial != 0 && !Same(s, SnapshotOf(s.serial)))
        ++t.torn;
    }
    b::Status st;
    if (v.ReadStatus(&st, 1000))
    {
      ++t.reads;
      if (st.flags != st.step || st.step != st.stop_seen || st.step != st.notice_seen ||
          (st.step != 0 && st.phase != static_cast<b::Phase>(st.step % 12)))
        ++t.torn;
    }
    const std::uint64_t head = v.EventsHead();
    for (; next < head; ++next)
    {
      b::Event e;
      const b::Read r = v.ReadEvents(next, &e);
      if (r == b::Read::Ok)
        t.events += 1, t.bad_events += SameEvent(e, next) ? 0 : 1;
      else if (r == b::Read::Lost)
        ++t.lost;
      else
        break;
    }
  }
  return t;
}

void Report(const char* what, const Tally& t)
{
  std::printf("board %s: %ld reads, %ld torn; %ld events read, %ld wrong, %ld lost to overwriting\n", what, t.reads,
              t.torn, t.events, t.bad_events, t.lost);
  CHECK(t.reads > 1000 && t.torn == 0 && t.events > 100 && t.bad_events == 0);
}

void Threads()
{
  Memory m;
  b::View v(m.bytes);
  std::thread w([v] { Writer(v, std::chrono::milliseconds(700)); });
  const Tally t = Reader(v);
  w.join();
  Report("two threads", t);
}

void Processes()
{
  char path[] = "/tmp/fzgx-board-XXXXXX";
  const int fd = mkstemp(path);
  CHECK(fd >= 0 && ftruncate(fd, b::SIZE) == 0);
  void* at = mmap(nullptr, b::SIZE, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
  CHECK(at != MAP_FAILED);
  b::View v(at);
  v.Make(static_cast<std::uint32_t>(getpid()));
  const pid_t child = fork();
  if (child == 0)
  {
    // the game's side: its own mapping of the same file
    const int own = open(path, O_RDWR);
    void* mine = mmap(nullptr, b::SIZE, PROT_READ | PROT_WRITE, MAP_SHARED, own, 0);
    b::View g(mine);
    if (g.Take(static_cast<std::uint32_t>(getpid()), b::SIZE) != b::View::Taken::Ok)
      _exit(3);
    Writer(g, std::chrono::milliseconds(700));
    _exit(0);
  }
  const Tally t = Reader(v);
  int status = 0;
  waitpid(child, &status, 0);
  CHECK(WIFEXITED(status) && WEXITSTATUS(status) == 0);
  CHECK(v.HeaderRunnerPid() == static_cast<std::uint32_t>(child));
  Report("two processes", t);
  munmap(at, b::SIZE);
  close(fd);
  unlink(path);
}

// The known board: what --write leaves for the other languages' tests to read.
void Known(b::View v)
{
  v.Make(4242);
  CHECK(v.Take(4343, b::SIZE) == b::View::Taken::Ok);
  v.SetPulseHeartbeat(0x0102030405060708ull);
  v.SetPulseWatchBeat(99);
  v.SetPulseStopReason(b::StopReason::Escape);
  b::Status st;
  st.phase = b::Phase::Finished;
  st.flags = b::STATUS_FLAGS_PRACTICE;
  st.step = 5000;
  st.stop_seen = 1;
  st.notice_seen = 2;
  v.WriteStatus(st);
  v.WriteSnapshot(SnapshotOf(321));
  for (std::uint64_t n = 0; n < 70; ++n)
    v.WriteEvents(n, EventOf(n));
  b::Mailbox mail;
  mail.stop_serial = 3;
  mail.notice_serial = 5;
  mail.notice_length = 5;
  std::memcpy(mail.notice_text.data(), "héllo", 6);
  mail.notice_length = 6;
  v.WriteMailbox(mail);
}

// What the C# test's --write leaves: the same known board, made there.
void CheckKnown(b::View v)
{
  CHECK(v.HeaderMagic() == b::MAGIC && v.HeaderLauncherPid() == 4242 && v.HeaderRunnerPid() == 4343);
  CHECK(v.PulseHeartbeat() == 0x0102030405060708ull && v.PulseWatchBeat() == 99 &&
        v.PulseStopReason() == b::StopReason::Escape);
  b::Status st;
  CHECK(v.ReadStatus(&st) && st.phase == b::Phase::Finished && st.flags == 2 && st.step == 5000 && st.stop_seen == 1 &&
        st.notice_seen == 2);
  b::Snapshot s;
  CHECK(v.ReadSnapshot(&s) && Same(s, SnapshotOf(321)));
  CHECK(v.EventsHead() == 70);
  b::Event e;
  CHECK(v.ReadEvents(5, &e) == b::Read::Lost && v.ReadEvents(6, &e) == b::Read::Ok && SameEvent(e, 6));
  CHECK(v.ReadEvents(69, &e) == b::Read::Ok && SameEvent(e, 69));
  b::Mailbox mail;
  CHECK(v.ReadMailbox(&mail) && mail.stop_serial == 3 && mail.notice_serial == 5 && mail.notice_length == 6 &&
        std::memcmp(mail.notice_text.data(), "héllo", 6) == 0);
}

bool FileBoard(const char* path, bool write)
{
  std::vector<unsigned char> bytes(b::SIZE + 64);
  auto* at = reinterpret_cast<unsigned char*>((reinterpret_cast<std::uintptr_t>(bytes.data()) + 63) & ~std::uintptr_t{63});
  if (write)
  {
    std::memset(at, 0, b::SIZE);
    Known(b::View(at));
    FILE* f = std::fopen(path, "wb");
    return f && std::fwrite(at, 1, b::SIZE, f) == b::SIZE && std::fclose(f) == 0;
  }
  FILE* f = std::fopen(path, "rb");
  if (!f || std::fread(at, 1, b::SIZE, f) != b::SIZE)
    return false;
  std::fclose(f);
  CheckKnown(b::View(at));
  return true;
}
}  // namespace

// --serve: the game's side for the C# reader (BoardCheck --live), in a process of its own.
int Serve(const char* path)
{
  const int fd = open(path, O_RDWR);
  void* at = fd < 0 ? MAP_FAILED : mmap(nullptr, b::SIZE, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
  if (at == MAP_FAILED)
    return 3;
  b::View g(at);
  if (g.Take(static_cast<std::uint32_t>(getpid()), b::SIZE) != b::View::Taken::Ok)
    return 4;
  Writer(g, std::chrono::milliseconds(1500));  // (the C# reader's first reads are compiled as it goes)
  munmap(at, b::SIZE);
  close(fd);
  return 0;
}

int main(int argc, char** argv)
{
  if (argc == 3 && std::string(argv[1]) == "--serve")
    return Serve(argv[2]);
  if (argc == 3 && (std::string(argv[1]) == "--write" || std::string(argv[1]) == "--read"))
  {
    if (!FileBoard(argv[2], std::string(argv[1]) == "--write"))
      std::fprintf(stderr, "board: cannot use %s\n", argv[2]), ++s_failed;
    std::printf("board %s %s: %s\n", argv[1] + 2, argv[2], s_failed ? "FAIL" : "ok");
    return s_failed ? 1 : 0;
  }
  Header();
  RoundTrips();
  RingOverflow();
  {
    Memory m;
    b::View v(m.bytes);
    Known(v);
    CheckKnown(v);
  }
  Threads();
  Processes();
  std::printf("board C++: %s\n", s_failed ? "FAIL" : "ok");
  return s_failed ? 1 : 0;
}
