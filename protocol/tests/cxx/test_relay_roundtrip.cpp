// The generated C++ header (protocol/generated/relay_packets.g.h) over the vectors file, both directions: each
// datagram decodes to the packet the file records (- none) with the foreign version it records, and each packet
// encodes back to the very bytes. The file is the wire of relay version 3 as 0.4.0's hand-written C# codec decoded it
// (recorded by the parity tests that compared the codecs before the swap); the
// game's own packets are among its lines, so a change of the wire the game speaks fails here.
//   test_relay_roundtrip VECTORS.tsv
// SPDX-License-Identifier: GPL-3.0-or-later

#include "relay_describe.g.h"
#include "relay_packets.g.h"

#include <cstdio>
#include <fstream>
#include <set>
#include <string>
#include <vector>

namespace
{
namespace relay = fzgx::net::relay;
using Bytes = std::vector<std::uint8_t>;

int failures = 0;

void Fail(const std::string& what)
{
  if (++failures <= 20)
    std::fprintf(stderr, "cxx round trip: %s\n", what.c_str());
}

Bytes FromHex(const std::string& hex)
{
  Bytes out;
  for (std::size_t i = 0; i + 1 < hex.size(); i += 2)
    out.push_back(static_cast<std::uint8_t>(std::stoul(hex.substr(i, 2), nullptr, 16)));
  return out;
}

// One line of the vectors file: bytes, the packet they decode to (- none), the foreign version (- none).
bool CheckLine(const std::string& line, int* packets, std::set<std::size_t>* kinds)
{
  const auto tab1 = line.find('\t');
  const auto tab2 = line.find('\t', tab1 + 1);
  if (tab1 == std::string::npos || tab2 == std::string::npos)
    return false;
  const Bytes bytes = FromHex(line.substr(0, tab1));
  const std::string want = line.substr(tab1 + 1, tab2 - tab1 - 1), want_foreign = line.substr(tab2 + 1);
  const auto packet = relay::Decode(bytes.data(), bytes.size());
  const std::string got = packet ? relay::test::Describe(*packet) : "-";
  if (got != want)
    Fail("decode " + line.substr(0, tab1) + ": [" + got + "], the vectors [" + want + "]");
  const auto foreign = relay::ForeignVersion(bytes.data(), bytes.size());
  if ((foreign ? std::to_string(*foreign) : "-") != want_foreign)
    Fail("foreign version of " + line.substr(0, tab1));
  if (!packet)
    return true;
  ++*packets;
  kinds->insert(packet->index());
  if (relay::Encode(*packet) != bytes)
    Fail("decode then encode differs: " + line.substr(0, tab1));
  return true;
}

// Packets whose count and records disagree are written as they are, and such bytes decode to nothing.
void CheckDisagreeing()
{
  const relay::Input short_records{.token = 1, .count = 2, .records = Bytes(5, 7)};
  const relay::Input none{.token = 1, .count = 255, .records = {}};
  for (const auto& p : {short_records, none})
  {
    const Bytes bytes = relay::Encode(p);
    if (bytes.size() != relay::HEADER_SIZE + 25 + p.records.size())
      Fail("an input packet whose count and records disagree is not written as it is");
    if (relay::Decode(bytes.data(), bytes.size()))
      Fail("an input packet whose count and records disagree decodes");
  }
}
}  // namespace

int main(int argc, char** argv)
{
  if (argc != 2)
  {
    std::fprintf(stderr, "usage: test_relay_roundtrip VECTORS.tsv\n");
    return 2;
  }
  std::ifstream file(argv[1]);
  if (!file)
  {
    std::fprintf(stderr, "cxx round trip: cannot read %s\n", argv[1]);
    return 2;
  }
  int lines = 0, packets = 0;
  std::set<std::size_t> kinds;
  for (std::string line; std::getline(file, line);)
  {
    if (line.empty() || line[0] == '#')
      continue;
    ++lines;
    if (!CheckLine(line, &packets, &kinds))
      Fail("not a vectors line: " + line.substr(0, 40));
  }
  CheckDisagreeing();
  if (kinds.size() != std::variant_size_v<relay::Packet>)
    Fail("the vectors hold " + std::to_string(kinds.size()) + " kinds of packet, not every one");
  std::printf("cxx round trip: %d vectors, %d of them packets (%zu kinds); %d differences\n", lines, packets,
              kinds.size(), failures);
  return failures == 0 && lines > 0 ? 0 : 1;
}
