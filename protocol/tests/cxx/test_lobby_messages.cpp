// The generated C++ codec of the lobby link (generated/lobby_messages.g.h) over the vectors that C# wrote
// (tests/vectors/messages/messages.tsv, tests/dotnet-messages): every line's input is read as C# reads it and written
// again as C# writes it, byte for byte ("=": the input itself; "!": no message; "~": a message that cannot be written).
// One difference is allowed and counted: a null item of a list of texts or records, which C# keeps and the C++ struct
// drops (json_wire.h). With --write FILE it also writes what it wrote of the lines C# writes as it reads them, and
// messages it makes itself, as vector lines for C# to read back to the same bytes (MessageVectors check FILE: the
// other direction).
//   test_lobby_messages VECTORS [--write FILE]
#include <cstdio>
#include <fstream>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>

#include "lobby_messages.g.h"

namespace lobby = fzgx::net::lobby;
namespace jw = fzgx::json_wire;

namespace
{
// What the C++ codec makes of an input of a root: its writing, "!" (no message) or "~" (cannot be written).
std::string Read(const std::string& root, const std::string& input)
{
  std::string out;
  if (root == "client")
  {
    auto m = lobby::DecodeClient(input);
    if (!m)
      return "!";
    out = lobby::Encode(*m);
  }
  else if (root == "server")
  {
    auto m = lobby::DecodeServer(input);
    if (!m)
      return "!";
    out = lobby::Encode(*m);
  }
  else if (root == "lan")
  {
    auto m = lobby::DecodeLanAnnouncement(input);
    if (!m)
      return "!";
    out = lobby::Encode(*m);
  }
  else if (root == "status")
  {
    auto m = lobby::DecodeServiceStatus(input);
    if (!m)
      return "!";
    out = lobby::Encode(*m);
  }
  else
    return "?";
  return out.empty() ? "~" : out;
}

// A document written again with every null item of its arrays left out (what C++ keeps of such lists).
void Canon(const jw::Value& v, jw::Out& o)
{
  switch (v.kind)
  {
  case jw::Value::Kind::Null: o.text += "null"; break;
  case jw::Value::Kind::True: o.text += "true"; break;
  case jw::Value::Kind::False: o.text += "false"; break;
  case jw::Value::Kind::Number: o.text += v.text; break;
  case jw::Value::Kind::String: jw::PutString(o, v.text); break;
  case jw::Value::Kind::Array:
  {
    o.text += '[';
    bool first = true;
    for (const auto& item : v.items)
    {
      if (item.kind == jw::Value::Kind::Null)
        continue;
      if (!first)
        o.text += ',';
      first = false;
      Canon(item, o);
    }
    o.text += ']';
    break;
  }
  case jw::Value::Kind::Object:
  {
    o.text += '{';
    bool first = true;
    for (const auto& [k, item] : v.members)
    {
      jw::Key(o, first, k);
      Canon(item, o);
    }
    o.text += '}';
    break;
  }
  }
}

std::string WithoutNullItems(const std::string& text)
{
  jw::Value v;
  if (!jw::Parse(text, v))
    return "(no JSON)";
  jw::Out o;
  Canon(v, o);
  return o.text;
}

std::vector<std::string> Split(const std::string& line)
{
  std::vector<std::string> f;
  std::size_t at = 0;
  while (true)
  {
    const std::size_t tab = line.find('\t', at);
    f.push_back(line.substr(at, tab == std::string::npos ? std::string::npos : tab - at));
    if (tab == std::string::npos)
      return f;
    at = tab + 1;
  }
}

// Messages the C++ side makes itself (no C# wrote them): for C# to read back to the same bytes.
std::vector<std::pair<std::string, std::string>> Made()
{
  std::vector<std::pair<std::string, std::string>> out;
  lobby::Hello hello;
  hello.name = "N\xC3\xA9o \xF0\x9F\x98\x80 <&>";
  hello.version = lobby::kVersion;
  hello.build = "0123456789abcdef/fedcba9876543210/00112233";
  hello.voteCounts = hello.statPacks = hello.pings = hello.notices = true;
  hello.key = "Zm9vYmFyLWtleS0zMi1ieXRlcy1sb25nLXRleHQ";
  hello.line = "0.7";
  hello.modes = 2;
  out.emplace_back("client", lobby::Encode(hello));
  lobby::EnterQuickPlay queue;
  queue.machine = 12;
  queue.color = {255, 0, 128};
  queue.setting = 75;
  queue.trail = lobby::BoostTrail{lobby::Rgb{1, 2, 3}, std::nullopt, lobby::Rgb{0, 0, 0}, 0, 0};
  queue.custom = lobby::CustomMachine{3, 30, 60, 40, std::vector<std::uint32_t>{0xFF000001u, 0, 0x00FF0001u}, 0, 0};
  out.emplace_back("client", lobby::Encode(queue));
  lobby::RaceReport report;
  report.matchId = "m-\t1";
  report.snapshot.frame = 3600;
  report.snapshot.localSeat = 4;
  report.snapshot.scoreKind = "points";
  for (int i = 0; i < 3; ++i)
  {
    lobby::RacerLive r;
    r.seat = i;
    r.speed = 1234.5678f * static_cast<float>(i + 1);
    r.energy = i == 0 ? 1e-7f : i == 1 ? 3.4028235e38f : -0.0f;
    r.maxEnergy = 100.0f;
    r.topSpeed = 1e9f;
    r.state = i == 2 ? "out" : "racing";
    r.x = 0.1f;
    r.y = -2.5e-38f;
    r.z = 16777217.0f;
    r.distance = 99999.99f;
    r.bestLapMs = i == 1 ? std::optional<std::int32_t>(61234) : std::nullopt;
    r.score = i == 0 ? std::optional<float>(7.0f) : std::nullopt;
    report.snapshot.racers.push_back(r);
  }
  out.emplace_back("client", lobby::Encode(report));
  lobby::RaceOver over;
  over.matchId = "m1";
  over.final = report.snapshot;
  out.emplace_back("client", lobby::Encode(over));
  lobby::RelayLatency latency;
  latency.matchId = "m1";
  latency.results = {{"fra", 12.25, 0, 0}, {"iad", std::nullopt, 0, 0}, {"syd", 287.0000000001, 0, 0}};
  out.emplace_back("client", lobby::Encode(latency));
  lobby::ChatMessage chat;
  chat.channel = "global";
  chat.fromId = "server";
  chat.fromName = "SYSTEM";
  chat.text = "line\nbreak \x01 \x7F \xFF (a byte that is no UTF-8)";
  chat.time = {jw::detail::Days(2026, 11, 14) * jw::kTicksPerDay + 1234567, -330};
  chat.level = "urgent";
  out.emplace_back("server", lobby::Encode(chat));
  return out;
}
}  // namespace

int main(int argc, char** argv)
{
  if (argc != 2 && !(argc == 4 && std::string(argv[2]) == "--write"))
  {
    std::cerr << "usage: test_lobby_messages VECTORS [--write FILE]\n";
    return 2;
  }
  std::ifstream in(argv[1], std::ios::binary);
  if (!in)
  {
    std::cerr << "lobby vectors: cannot read " << argv[1] << "\n";
    return 1;
  }
  std::vector<std::string> written;
  int lines = 0, exact = 0, holes = 0, failures = 0, refused = 0;
  std::string line;
  while (std::getline(in, line))
  {
    if (line.empty() || line[0] == '#')
      continue;
    const auto f = Split(line);
    if (f.size() != 4)
    {
      std::cerr << "lobby vectors: not four columns: " << line << "\n";
      ++failures;
      continue;
    }
    ++lines;
    const std::string got = Read(f[0], f[2]);
    const std::string want = f[3] == "=" ? f[2] : f[3];
    if (got == want)
      ++exact, refused += got == "!" || got == "~";
    else if (got.size() > 1 && want.size() > 1 && want.find("null") != std::string::npos &&
             WithoutNullItems(want) == WithoutNullItems(got))
      ++holes;
    else
    {
      if (++failures <= 20)
        std::cerr << "lobby vectors: " << f[0] << " " << f[1] << "\n  input    " << f[2] << "\n  C++      " << got
                  << "\n  expected " << want << "\n";
    }
    if (f[3] == "=" && got.size() > 1)
      written.push_back(f[0] + "\t" + f[1] + "\t" + got + "\t=");   // (what C# writes as it read it)
  }
  int made = 0;
  for (const auto& [root, text] : Made())
  {
    // each made message reads back to itself here too
    if (text.empty() || Read(root, text) != text)
    {
      std::cerr << "lobby vectors: a made " << root << " message does not read back: " << text << "\n";
      ++failures;
    }
    written.push_back(root + "\tmade\t" + text + "\t=");
    ++made;
  }
  if (argc == 4)
  {
    std::ofstream out(argv[3], std::ios::binary);
    for (const auto& w : written)
      out << w << "\n";
  }
  std::cout << "lobby vectors (C++): " << lines << " lines, " << exact << " byte for byte (" << refused
            << " refused as C# refuses them), " << holes << " with a list's null item dropped, " << failures
            << " differences; " << made << " made messages\n";
  return failures == 0 && lines > 0 ? 0 : 1;
}
