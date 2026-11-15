// JSON as the lobby link writes and reads it (protocol/messages.toml): what System.Text.Json does with
// Online.Protocol's ControlProtocol.Json (the web defaults, a null member left out), byte for byte, for the generated
// codec generated/lobby_messages.g.h. Not generated: the generated header holds the messages, this one the JSON.
//
// Reading: RFC 8259 and nothing else (no comments, no trailing commas, no byte order mark), at most 64 arrays and
// objects deep, strings of valid UTF-8 whose \u escapes pair their surrogates; a member's name matched without regard
// to ASCII case, the last of a name winning; a number into a whole number only when it has no fraction and no
// exponent and fits, or from a string ("+3", "03"; an unsigned type takes no sign); a float from a number or a
// string ("NaN", "Infinity", "-Infinity", ".5", "5.", "+2.5"), a number beyond the type's range as an infinity or
// zero; an enum written by name by a name (any case, names joined by commas or), a number or a number in a string.
// Writing: no spaces; every character outside printable ASCII, and " & ' + < > `, as \uXXXX (upper case hex) but
// \b \t \n \f \r \\; numbers as .NET writes them (a float or double in the fewest digits that read back to it, with an
// exponent "E+XX" when it is 1e-5 or smaller or has more whole digits than 9 (float) or 17 (double)); a time as
// "yyyy-MM-ddTHH:mm:ss[.fffffff]+HH:MM", the fraction without its trailing zeros. A float that is not finite cannot be
// written (System.Text.Json refuses it): the message is then not written at all.
//
// Known differences from C#, none of which a writer of either side produces: a member name with a character that
// .NET's case folding takes for an ASCII letter (dotless i, long s) does not match here; a time with no offset is
// read as +00:00 (C# takes the reading computer's own); a null item of a list of texts or records is dropped (C#
// keeps it: the struct says so in wire_holes).
// SPDX-License-Identifier: GPL-3.0-or-later

#pragma once

#include <algorithm>
#include <charconv>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>
#include <optional>
#include <string>
#include <string_view>
#include <type_traits>
#include <utility>
#include <vector>

namespace fzgx::json_wire
{
// ---------------------------------------------------------------- the document

struct Value
{
  enum class Kind : std::uint8_t { Null, False, True, Number, String, Array, Object };
  Kind kind = Kind::Null;
  std::string text;                                   // a number as written, or a string's characters (UTF-8)
  std::vector<Value> items;                           // an array's
  std::vector<std::pair<std::string, Value>> members; // an object's, in order (a name twice: both)
};

inline constexpr int kMaxDepth = 64;

namespace detail
{
class Parser
{
public:
  explicit Parser(std::string_view in) : in_(in) {}

  bool Document(Value& out)
  {
    Space();
    if (!Any(out, 0))
      return false;
    Space();
    return at_ == in_.size();
  }

private:
  std::string_view in_;
  std::size_t at_ = 0;

  void Space()
  {
    while (at_ < in_.size() && (in_[at_] == ' ' || in_[at_] == '\t' || in_[at_] == '\n' || in_[at_] == '\r'))
      ++at_;
  }
  bool Literal(std::string_view word)
  {
    if (in_.substr(at_, word.size()) != word)
      return false;
    at_ += word.size();
    return true;
  }
  static bool Digit(char c) { return c >= '0' && c <= '9'; }

  bool Any(Value& out, int depth)
  {
    if (at_ >= in_.size())
      return false;
    switch (in_[at_])
    {
    case 'n': out.kind = Value::Kind::Null; return Literal("null");
    case 't': out.kind = Value::Kind::True; return Literal("true");
    case 'f': out.kind = Value::Kind::False; return Literal("false");
    case '"': out.kind = Value::Kind::String; return String(out.text);
    case '[': return Array(out, depth + 1);
    case '{': return Object(out, depth + 1);
    default: out.kind = Value::Kind::Number; return Number(out.text);
    }
  }

  bool Number(std::string& out)
  {
    const std::size_t start = at_;
    if (at_ < in_.size() && in_[at_] == '-')
      ++at_;
    if (at_ >= in_.size() || !Digit(in_[at_]))
      return false;
    if (in_[at_] == '0')
      ++at_;
    else
      while (at_ < in_.size() && Digit(in_[at_]))
        ++at_;
    if (at_ < in_.size() && in_[at_] == '.')
    {
      ++at_;
      if (at_ >= in_.size() || !Digit(in_[at_]))
        return false;
      while (at_ < in_.size() && Digit(in_[at_]))
        ++at_;
    }
    if (at_ < in_.size() && (in_[at_] == 'e' || in_[at_] == 'E'))
    {
      ++at_;
      if (at_ < in_.size() && (in_[at_] == '+' || in_[at_] == '-'))
        ++at_;
      if (at_ >= in_.size() || !Digit(in_[at_]))
        return false;
      while (at_ < in_.size() && Digit(in_[at_]))
        ++at_;
    }
    out.assign(in_.substr(start, at_ - start));
    return true;
  }

  static void Utf8(std::string& out, std::uint32_t c)
  {
    if (c < 0x80)
      out += static_cast<char>(c);
    else if (c < 0x800)
    {
      out += static_cast<char>(0xC0 | (c >> 6));
      out += static_cast<char>(0x80 | (c & 0x3F));
    }
    else if (c < 0x10000)
    {
      out += static_cast<char>(0xE0 | (c >> 12));
      out += static_cast<char>(0x80 | ((c >> 6) & 0x3F));
      out += static_cast<char>(0x80 | (c & 0x3F));
    }
    else
    {
      out += static_cast<char>(0xF0 | (c >> 18));
      out += static_cast<char>(0x80 | ((c >> 12) & 0x3F));
      out += static_cast<char>(0x80 | ((c >> 6) & 0x3F));
      out += static_cast<char>(0x80 | (c & 0x3F));
    }
  }

  bool Hex4(std::uint32_t& out)
  {
    if (at_ + 4 > in_.size())
      return false;
    out = 0;
    for (int i = 0; i < 4; ++i)
    {
      const char c = in_[at_++];
      out <<= 4;
      if (c >= '0' && c <= '9')
        out |= static_cast<std::uint32_t>(c - '0');
      else if (c >= 'a' && c <= 'f')
        out |= static_cast<std::uint32_t>(c - 'a' + 10);
      else if (c >= 'A' && c <= 'F')
        out |= static_cast<std::uint32_t>(c - 'A' + 10);
      else
        return false;
    }
    return true;
  }

  // one character of UTF-8 as it is written (valid: no overlong form, no surrogate, nothing past U+10FFFF)
  bool Raw(std::string& out)
  {
    const auto b = static_cast<unsigned char>(in_[at_]);
    int more = 0;
    std::uint32_t c = 0, least = 0;
    if (b < 0x80)
    {
      out += static_cast<char>(b);
      ++at_;
      return true;
    }
    if (b >= 0xC2 && b <= 0xDF)
      more = 1, c = b & 0x1F, least = 0x80;
    else if (b >= 0xE0 && b <= 0xEF)
      more = 2, c = b & 0x0F, least = 0x800;
    else if (b >= 0xF0 && b <= 0xF4)
      more = 3, c = b & 0x07, least = 0x10000;
    else
      return false;
    if (at_ + 1 + static_cast<std::size_t>(more) > in_.size())
      return false;
    for (int i = 1; i <= more; ++i)
    {
      const auto n = static_cast<unsigned char>(in_[at_ + static_cast<std::size_t>(i)]);
      if ((n & 0xC0) != 0x80)
        return false;
      c = (c << 6) | (n & 0x3F);
    }
    if (c < least || c > 0x10FFFF || (c >= 0xD800 && c <= 0xDFFF))
      return false;
    out.append(in_.substr(at_, static_cast<std::size_t>(more) + 1));
    at_ += static_cast<std::size_t>(more) + 1;
    return true;
  }

  bool String(std::string& out)
  {
    ++at_;   // "
    out.clear();
    while (true)
    {
      if (at_ >= in_.size())
        return false;
      const char c = in_[at_];
      if (c == '"')
      {
        ++at_;
        return true;
      }
      if (static_cast<unsigned char>(c) < 0x20)
        return false;
      if (c != '\\')
      {
        if (!Raw(out))
          return false;
        continue;
      }
      if (++at_ >= in_.size())
        return false;
      switch (in_[at_++])
      {
      case '"': out += '"'; break;
      case '\\': out += '\\'; break;
      case '/': out += '/'; break;
      case 'b': out += '\b'; break;
      case 'f': out += '\f'; break;
      case 'n': out += '\n'; break;
      case 'r': out += '\r'; break;
      case 't': out += '\t'; break;
      case 'u':
      {
        std::uint32_t u = 0;
        if (!Hex4(u))
          return false;
        if (u >= 0xDC00 && u <= 0xDFFF)
          return false;   // a low surrogate alone
        if (u >= 0xD800 && u <= 0xDBFF)
        {
          std::uint32_t low = 0;
          if (at_ + 2 > in_.size() || in_[at_] != '\\' || in_[at_ + 1] != 'u')
            return false;
          at_ += 2;
          if (!Hex4(low) || low < 0xDC00 || low > 0xDFFF)
            return false;
          u = 0x10000 + ((u - 0xD800) << 10) + (low - 0xDC00);
        }
        Utf8(out, u);
        break;
      }
      default: return false;
      }
    }
  }

  bool Array(Value& out, int depth)
  {
    if (depth > kMaxDepth)
      return false;
    out.kind = Value::Kind::Array;
    ++at_;   // [
    Space();
    if (at_ < in_.size() && in_[at_] == ']')
    {
      ++at_;
      return true;
    }
    while (true)
    {
      out.items.emplace_back();
      Space();
      if (!Any(out.items.back(), depth))
        return false;
      Space();
      if (at_ >= in_.size())
        return false;
      if (in_[at_] == ']')
      {
        ++at_;
        return true;
      }
      if (in_[at_++] != ',')
        return false;
    }
  }

  bool Object(Value& out, int depth)
  {
    if (depth > kMaxDepth)
      return false;
    out.kind = Value::Kind::Object;
    ++at_;   // {
    Space();
    if (at_ < in_.size() && in_[at_] == '}')
    {
      ++at_;
      return true;
    }
    while (true)
    {
      Space();
      if (at_ >= in_.size() || in_[at_] != '"')
        return false;
      out.members.emplace_back();
      if (!String(out.members.back().first))
        return false;
      Space();
      if (at_ >= in_.size() || in_[at_++] != ':')
        return false;
      Space();
      if (!Any(out.members.back().second, depth))
        return false;
      Space();
      if (at_ >= in_.size())
        return false;
      if (in_[at_] == '}')
      {
        ++at_;
        return true;
      }
      if (in_[at_++] != ',')
        return false;
    }
  }
};
}  // namespace detail

// The document of a text; false when it is no JSON as the lobby link reads it.
inline bool Parse(std::string_view text, Value& out)
{
  out = Value{};
  return detail::Parser(text).Document(out);
}

// ---------------------------------------------------------------- a time

// A DateTimeOffset: the clock time where it was taken (100 ns ticks from 0001-01-01T00:00:00) and its offset from UTC.
struct Time
{
  std::int64_t ticks = 0;
  std::int32_t offsetMinutes = 0;
};

inline constexpr std::int64_t kTicksPerSecond = 10'000'000, kTicksPerDay = 86'400 * kTicksPerSecond;
inline constexpr std::int64_t kMaxTicks = 3'155'378'975'999'999'999;   // 9999-12-31T23:59:59.9999999

namespace detail
{
// days from 0001-01-01 of a date of the proleptic Gregorian calendar (H. Hinnant's days_from_civil, moved)
inline std::int64_t Days(std::int64_t y, unsigned m, unsigned d)
{
  y -= m <= 2;
  const std::int64_t era = (y >= 0 ? y : y - 399) / 400;
  const auto yoe = static_cast<unsigned>(y - era * 400);
  const unsigned doy = (153 * (m > 2 ? m - 3 : m + 9) + 2) / 5 + d - 1;
  const unsigned doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
  return era * 146097 + static_cast<std::int64_t>(doe) - 719468 + 719162;
}

inline void Civil(std::int64_t days, std::int64_t& y, unsigned& m, unsigned& d)
{
  const std::int64_t z = days - 719162 + 719468;
  const std::int64_t era = (z >= 0 ? z : z - 146096) / 146097;
  const auto doe = static_cast<unsigned>(z - era * 146097);
  const unsigned yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
  const unsigned doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
  const unsigned mp = (5 * doy + 2) / 153;
  d = doy - (153 * mp + 2) / 5 + 1;
  m = mp < 10 ? mp + 3 : mp - 9;
  y = static_cast<std::int64_t>(yoe) + era * 400 + (m <= 2);
}

inline bool Digits(std::string_view s, std::size_t at, std::size_t n, unsigned& out)
{
  if (at + n > s.size())
    return false;
  out = 0;
  for (std::size_t i = at; i < at + n; ++i)
  {
    if (s[i] < '0' || s[i] > '9')
      return false;
    out = out * 10 + static_cast<unsigned>(s[i] - '0');
  }
  return true;
}
}  // namespace detail

// A time as System.Text.Json reads a DateTimeOffset: yyyy-MM-dd, then THH:mm, :ss and a fraction of up to 16 digits
// (the first 7 kept), then Z or +HH or +HH:mm (at most 14:00).
inline bool ParseTime(std::string_view s, Time& out)
{
  unsigned y = 0, mo = 0, d = 0, h = 0, mi = 0, se = 0;
  if (!detail::Digits(s, 0, 4, y) || s.size() < 10 || s[4] != '-' || !detail::Digits(s, 5, 2, mo) || s[7] != '-' ||
      !detail::Digits(s, 8, 2, d))
    return false;
  static constexpr unsigned kMonth[] = {31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
  const bool leap = (y % 4 == 0 && y % 100 != 0) || y % 400 == 0;
  if (y < 1 || mo < 1 || mo > 12 || d < 1 || d > kMonth[mo - 1] + (mo == 2 && leap ? 1u : 0u))
    return false;
  std::size_t at = 10;
  std::int64_t fraction = 0;
  if (at < s.size())
  {
    if (s[at] != 'T' || !detail::Digits(s, at + 1, 2, h) || at + 3 >= s.size() || s[at + 3] != ':' ||
        !detail::Digits(s, at + 4, 2, mi))
      return false;
    at += 6;
    if (at < s.size() && s[at] == ':')
    {
      if (!detail::Digits(s, at + 1, 2, se))
        return false;
      at += 3;
      if (at < s.size() && s[at] == '.')
      {
        ++at;
        std::size_t digits = 0;
        while (at < s.size() && s[at] >= '0' && s[at] <= '9')
        {
          if (digits < 7)
            fraction = fraction * 10 + (s[at] - '0');
          ++digits, ++at;
        }
        if (digits > 16)
          return false;
        for (std::size_t i = digits; i < 7; ++i)
          fraction *= 10;
      }
    }
    if (h > 23 || mi > 59 || se > 59)
      return false;
  }
  std::int32_t offset = 0;
  if (at < s.size())
  {
    if (s[at] == 'Z')
      ++at;
    else if (s[at] == '+' || s[at] == '-')
    {
      unsigned oh = 0, om = 0;
      if (!detail::Digits(s, at + 1, 2, oh))
        return false;
      const bool minus = s[at] == '-';
      at += 3;
      if (at < s.size() && s[at] == ':')
      {
        if (!detail::Digits(s, at + 1, 2, om))
          return false;
        at += 3;
      }
      if (om > 59 || oh * 60 + om > 14 * 60)
        return false;
      offset = static_cast<std::int32_t>(oh * 60 + om) * (minus ? -1 : 1);
    }
    else
      return false;
  }
  if (at != s.size())
    return false;
  const std::int64_t ticks = detail::Days(y, mo, d) * kTicksPerDay +
                             static_cast<std::int64_t>(h * 3600 + mi * 60 + se) * kTicksPerSecond + fraction;
  const std::int64_t utc = ticks - std::int64_t{offset} * 60 * kTicksPerSecond;
  if (utc < 0 || utc > kMaxTicks)
    return false;
  out = Time{ticks, offset};
  return true;
}

inline std::string FormatTime(const Time& t)
{
  std::int64_t y = 0;
  unsigned m = 0, d = 0;
  const std::int64_t ticks = std::clamp<std::int64_t>(t.ticks, 0, kMaxTicks);
  detail::Civil(ticks / kTicksPerDay, y, m, d);
  const std::int64_t inDay = ticks % kTicksPerDay;
  const auto seconds = static_cast<unsigned>(inDay / kTicksPerSecond);
  const auto fraction = static_cast<unsigned>(inDay % kTicksPerSecond);
  char buf[48];
  int n = std::snprintf(buf, sizeof buf, "%04u-%02u-%02uT%02u:%02u:%02u", static_cast<unsigned>(y), m, d, seconds / 3600,
                        seconds / 60 % 60, seconds % 60);
  std::string out(buf, static_cast<std::size_t>(n));
  if (fraction != 0)
  {
    n = std::snprintf(buf, sizeof buf, ".%07u", fraction);
    std::string f(buf, static_cast<std::size_t>(n));
    while (f.back() == '0')
      f.pop_back();
    out += f;
  }
  const std::int32_t o = t.offsetMinutes;
  const auto a = static_cast<unsigned>(o < 0 ? -o : o);
  n = std::snprintf(buf, sizeof buf, "%c%02u:%02u", o < 0 ? '-' : '+', a / 60, a % 60);
  out.append(buf, static_cast<std::size_t>(n));
  return out;
}

// ---------------------------------------------------------------- reading a value

// A member's name against a record's names, without regard to ASCII case: its index, or -1.
template <std::size_t N>
int Find(std::string_view key, const std::string_view (&names)[N])
{
  for (std::size_t i = 0; i < N; ++i)
  {
    const std::string_view n = names[i];
    if (n.size() != key.size())
      continue;
    bool same = true;
    for (std::size_t j = 0; same && j < n.size(); ++j)
    {
      char a = key[j], b = n[j];
      if (a >= 'A' && a <= 'Z')
        a = static_cast<char>(a - 'A' + 'a');
      if (b >= 'A' && b <= 'Z')
        b = static_cast<char>(b - 'A' + 'a');
      same = a == b;
    }
    if (same)
      return static_cast<int>(i);
  }
  return -1;
}

namespace detail
{
// a whole number's text: [+-]digits (a string's; signed) or JSON's integer grammar (a number's: no fraction, no exponent)
template <typename T>
bool Whole(std::string_view text, bool quoted, T& out)
{
  if (text.empty())
    return false;
  if (!quoted)
  {
    if (text.find_first_of(".eE") != std::string_view::npos)
      return false;
  }
  else
  {
    if (text[0] == '+' && std::is_signed_v<T>)
      text.remove_prefix(1);
    else if (text[0] == '-' && (!std::is_signed_v<T> || text.size() < 2))
      return false;
    if (text.empty() || text[0] == '+' || (text[0] == '-' && text.size() < 2))
      return false;
    for (std::size_t i = text[0] == '-' ? 1 : 0; i < text.size(); ++i)
      if (text[i] < '0' || text[i] > '9')
        return false;
  }
  T v{};
  const auto r = std::from_chars(text.data(), text.data() + text.size(), v);
  if (r.ec != std::errc{} || r.ptr != text.data() + text.size())
    return false;
  out = v;
  return true;
}

// a float's text: JSON's number grammar, or a string's [+-](digits[.digits]|.digits)[e[+-]digits] or NaN, Infinity,
// -Infinity; beyond the type's range: an infinity (too large) or zero (too small), with the sign
template <typename T>
bool Real(std::string_view text, bool quoted, T& out)
{
  if (quoted)
  {
    if (text == "NaN")
      return out = std::numeric_limits<T>::quiet_NaN(), true;
    if (text == "Infinity")
      return out = std::numeric_limits<T>::infinity(), true;
    if (text == "-Infinity")
      return out = -std::numeric_limits<T>::infinity(), true;
    std::size_t i = 0, whole = 0, part = 0;
    if (i < text.size() && (text[i] == '+' || text[i] == '-'))
      ++i;
    if (i < text.size() && text[i] == '+')
      return false;
    while (i < text.size() && text[i] >= '0' && text[i] <= '9')
      ++i, ++whole;
    if (i < text.size() && text[i] == '.')
    {
      ++i;
      while (i < text.size() && text[i] >= '0' && text[i] <= '9')
        ++i, ++part;
    }
    if (whole + part == 0)
      return false;
    if (i < text.size() && (text[i] == 'e' || text[i] == 'E'))
    {
      ++i;
      if (i < text.size() && (text[i] == '+' || text[i] == '-'))
        ++i;
      std::size_t e = 0;
      while (i < text.size() && text[i] >= '0' && text[i] <= '9')
        ++i, ++e;
      if (e == 0)
        return false;
    }
    if (i != text.size())
      return false;
  }
  const bool minus = !text.empty() && text[0] == '-';
  if (!text.empty() && (text[0] == '+' || text[0] == '-'))
    text.remove_prefix(1);
  T v{};
  const auto r = std::from_chars(text.data(), text.data() + text.size(), v);
  if (r.ptr != text.data() + text.size())
    return false;
  if (r.ec == std::errc::result_out_of_range)
  {
    // too large or too small: which, by the place of the first digit that is not 0 against the exponent
    long exponent = 0;
    const std::size_t e = text.find_first_of("eE");
    if (e != std::string_view::npos)
    {
      std::string_view x = text.substr(e + 1);
      const bool neg = !x.empty() && x[0] == '-';
      if (!x.empty() && (x[0] == '-' || x[0] == '+'))
        x.remove_prefix(1);
      for (char c : x)
        exponent = std::min<long>(exponent * 10 + (c - '0'), 100'000'000L);
      exponent = neg ? -exponent : exponent;
    }
    const std::string_view mantissa = text.substr(0, e);
    const std::size_t dot = mantissa.find('.');
    const std::size_t first = mantissa.find_first_of("123456789");
    long place = 0;   // the power of ten of the first digit that is not 0
    if (first != std::string_view::npos)
    {
      const std::size_t point = dot == std::string_view::npos ? mantissa.size() : dot;
      place = first < point ? static_cast<long>(point - first - 1) : -static_cast<long>(first - point);
    }
    v = place + exponent > 0 ? std::numeric_limits<T>::infinity() : T(0);
  }
  else if (r.ec != std::errc{})
    return false;
  out = minus ? -v : v;
  return true;
}
}  // namespace detail

inline bool Get(const Value& x, bool& out)
{
  if (x.kind != Value::Kind::True && x.kind != Value::Kind::False)
    return false;
  out = x.kind == Value::Kind::True;
  return true;
}
inline bool Get(const Value& x, std::string& out)
{
  if (x.kind != Value::Kind::String)
    return false;
  out = x.text;
  return true;
}
template <typename T, std::enable_if_t<std::is_integral_v<T> && !std::is_same_v<T, bool>, int> = 0>
bool Get(const Value& x, T& out)
{
  if (x.kind != Value::Kind::Number && x.kind != Value::Kind::String)
    return false;
  return detail::Whole(x.text, x.kind == Value::Kind::String, out);
}
template <typename T, std::enable_if_t<std::is_floating_point_v<T>, int> = 0>
bool Get(const Value& x, T& out)
{
  if (x.kind != Value::Kind::Number && x.kind != Value::Kind::String)
    return false;
  return detail::Real(x.text, x.kind == Value::Kind::String, out);
}
inline bool Get(const Value& x, Time& out) { return x.kind == Value::Kind::String && ParseTime(x.text, out); }

// An enum written by name: a JSON whole number of the underlying type; or a string, trimmed: a number when it starts
// with a digit or a sign, else names joined by commas (each trimmed, any case; a comma at the end is passed over),
// their values or'd together (.NET's Enum.TryParse).
inline bool GetNamedEnum(const Value& x, const std::string_view* names, std::size_t count, std::int64_t least,
                         std::int64_t most, std::int64_t& out)
{
  std::int64_t v = 0;
  if (x.kind == Value::Kind::Number)
  {
    if (!detail::Whole(x.text, false, v))
      return false;
  }
  else if (x.kind == Value::Kind::String)
  {
    auto trim = [](std::string_view s) {
      while (!s.empty() && (s.front() == ' ' || s.front() == '\t' || s.front() == '\n' || s.front() == '\r'))
        s.remove_prefix(1);
      while (!s.empty() && (s.back() == ' ' || s.back() == '\t' || s.back() == '\n' || s.back() == '\r'))
        s.remove_suffix(1);
      return s;
    };
    std::string_view s = trim(x.text);
    if (s.empty())
      return false;
    if ((s[0] >= '0' && s[0] <= '9') || s[0] == '-' || s[0] == '+')
    {
      if (!detail::Whole(s, true, v))
        return false;
    }
    else
    {
      while (!s.empty())
      {
        const std::size_t comma = s.find(',');
        const std::string_view part = trim(s.substr(0, comma));
        int found = -1;
        for (std::size_t i = 0; i < count && found < 0; ++i)
        {
          const std::string_view n = names[i];
          if (n.size() != part.size())
            continue;
          bool same = true;
          for (std::size_t j = 0; same && j < n.size(); ++j)
          {
            char a = part[j], b = n[j];
            a = a >= 'A' && a <= 'Z' ? static_cast<char>(a - 'A' + 'a') : a;
            b = b >= 'A' && b <= 'Z' ? static_cast<char>(b - 'A' + 'a') : b;
            same = a == b;
          }
          found = same ? static_cast<int>(i) : -1;
        }
        if (found < 0)
          return false;
        v |= found;
        s = comma == std::string_view::npos ? std::string_view{} : s.substr(comma + 1);
      }
    }
  }
  else
    return false;
  if (v < least || v > most)
    return false;
  out = v;
  return true;
}

// An enum written as its number: a JSON whole number of the underlying type, never a string.
inline bool GetNumberEnum(const Value& x, std::int64_t least, std::int64_t most, std::int64_t& out)
{
  std::int64_t v = 0;
  if (x.kind != Value::Kind::Number || !detail::Whole(x.text, false, v) || v < least || v > most)
    return false;
  out = v;
  return true;
}

// The type of a value that C# never holds null (a number, a flag, an enum, a time): null where one is is no message.
template <typename T>
inline constexpr bool kIsValue = std::is_arithmetic_v<T> || std::is_enum_v<T> || std::is_same_v<T, Time>;

// A member that may be null.
template <typename T>
bool Get(const Value& x, std::optional<T>& out)
{
  if (x.kind == Value::Kind::Null)
  {
    out.reset();
    return true;
  }
  T v{};
  if (!Get(x, v))
    return false;
  out = std::move(v);
  return true;
}

// A list: null items of values are no message; of texts and records, dropped (*holes set).
template <typename T>
bool Get(const Value& x, std::vector<T>& out, bool* holes = nullptr)
{
  if (x.kind != Value::Kind::Array)
    return false;
  out.clear();
  out.reserve(x.items.size());
  for (const Value& item : x.items)
  {
    if (item.kind == Value::Kind::Null)
    {
      if constexpr (kIsValue<T>)
        return false;
      if (holes)
        *holes = true;
      continue;
    }
    T v{};
    if (!Get(item, v))
      return false;
    out.push_back(std::move(v));
  }
  return true;
}

// A map of string keys (a .NET dictionary: a key again takes the first one's place).
using Map = std::vector<std::pair<std::string, std::int32_t>>;
inline bool GetMap(const Value& x, Map& out)
{
  if (x.kind != Value::Kind::Object)
    return false;
  out.clear();
  for (const auto& [key, v] : x.members)
  {
    std::int32_t n = 0;
    if (!Get(v, n))
      return false;
    auto at = std::find_if(out.begin(), out.end(), [&](const auto& p) { return p.first == key; });
    if (at != out.end())
      at->second = n;
    else
      out.emplace_back(key, n);
  }
  return true;
}
inline bool GetMap(const Value& x, std::optional<Map>& out)
{
  if (x.kind == Value::Kind::Null)
    return out.reset(), true;
  Map m;
  if (!GetMap(x, m))
    return false;
  out = std::move(m);
  return true;
}

// A member C# holds as a reference that is no "?" (a text, a list, a map, a record): null sets its bit of *absent
// (C# reads null), a value clears it.
template <typename T, typename Read>
bool Held(const Value& x, T& out, std::uint32_t& absent, std::uint32_t bit, Read read)
{
  if (x.kind == Value::Kind::Null)
  {
    out = T{};
    absent |= bit;
    return true;
  }
  absent &= ~bit;
  return read(x, out);
}

// A message's type: the first member, named "type" exactly, a string, and no other member of that name.
inline bool Discriminator(const Value& x, std::string_view& out)
{
  if (x.kind != Value::Kind::Object || x.members.empty() || x.members[0].first != "type" ||
      x.members[0].second.kind != Value::Kind::String)
    return false;
  for (std::size_t i = 1; i < x.members.size(); ++i)
    if (x.members[i].first == "type")
      return false;
  out = x.members[0].second.text;
  return true;
}

// ---------------------------------------------------------------- writing

// What is written: the text, and whether it can be (a float not finite cannot).
struct Out
{
  std::string text;
  bool ok = true;
};

inline void PutString(Out& o, std::string_view s)
{
  static constexpr char kHex[] = "0123456789ABCDEF";
  auto u = [&](std::uint32_t c) {
    o.text += "\\u";
    for (int shift = 12; shift >= 0; shift -= 4)
      o.text += kHex[(c >> shift) & 0xF];
  };
  o.text += '"';
  for (std::size_t i = 0; i < s.size();)
  {
    const auto b = static_cast<unsigned char>(s[i]);
    if (b < 0x80)
    {
      switch (b)
      {
      case '\b': o.text += "\\b"; break;
      case '\t': o.text += "\\t"; break;
      case '\n': o.text += "\\n"; break;
      case '\f': o.text += "\\f"; break;
      case '\r': o.text += "\\r"; break;
      case '\\': o.text += "\\\\"; break;
      case '"': case '&': case '\'': case '+': case '<': case '>': case '`': u(b); break;
      default:
        if (b < 0x20 || b == 0x7F)
          u(b);
        else
          o.text += static_cast<char>(b);
      }
      ++i;
      continue;
    }
    // a character of UTF-8, as \u (a pair past U+FFFF); a byte that begins none, or a character cut short: U+FFFD
    const std::size_t more = b >= 0xC2 && b <= 0xDF ? 1 : b >= 0xE0 && b <= 0xEF ? 2 : b >= 0xF0 && b <= 0xF4 ? 3 : 0;
    std::uint32_t c = more == 1 ? (b & 0x1Fu) : more == 2 ? (b & 0x0Fu) : (b & 0x07u);
    bool good = more > 0 && i + more < s.size();
    for (std::size_t k = 1; good && k <= more; ++k)
    {
      const auto n = static_cast<unsigned char>(s[i + k]);
      good = (n & 0xC0) == 0x80;
      c = (c << 6) | (n & 0x3Fu);
    }
    const std::uint32_t least = more == 1 ? 0x80 : more == 2 ? 0x800 : 0x10000;
    if (!good || c < least || c > 0x10FFFF || (c >= 0xD800 && c <= 0xDFFF))
    {
      u(0xFFFD);
      ++i;
      continue;
    }
    if (c >= 0x10000)
    {
      u(0xD800 + ((c - 0x10000) >> 10));
      u(0xDC00 + ((c - 0x10000) & 0x3FF));
    }
    else
      u(c);
    i += more + 1;
  }
  o.text += '"';
}

template <typename T, std::enable_if_t<std::is_integral_v<T> && !std::is_same_v<T, bool>, int> = 0>
void Put(Out& o, T v)
{
  char buf[24];
  const auto r = std::to_chars(buf, buf + sizeof buf, v);
  o.text.append(buf, r.ptr);
}

// a float or a double as .NET writes it ("R": the fewest digits that read back to it; an exponent "E+XX" when the
// first digit's place is past `whole` digits or below 1e-4)
template <typename T, std::enable_if_t<std::is_floating_point_v<T>, int> = 0>
void Put(Out& o, T v)
{
  if (!std::isfinite(v))
  {
    o.ok = false;
    o.text += '0';
    return;
  }
  constexpr int kWhole = std::is_same_v<T, float> ? 9 : 17;
  char buf[64];
  const auto r = std::to_chars(buf, buf + sizeof buf, v, std::chars_format::scientific);
  std::string_view s(buf, static_cast<std::size_t>(r.ptr - buf));
  std::string out;
  if (s[0] == '-')
  {
    out += '-';
    s.remove_prefix(1);
  }
  const std::size_t e = s.find('e');
  std::string digits;
  for (char c : s.substr(0, e))
    if (c != '.')
      digits += c;
  int exponent = 0;
  std::from_chars(s.data() + e + (s[e + 1] == '+' ? 2 : 1), s.data() + s.size(), exponent);
  const int scale = exponent + 1;
  if (scale > kWhole || scale < -3)
  {
    out += digits[0];
    if (digits.size() > 1)
      out += '.' + digits.substr(1);
    char ex[16];
    std::snprintf(ex, sizeof ex, "E%c%02d", exponent < 0 ? '-' : '+', exponent < 0 ? -exponent : exponent);
    out += ex;
  }
  else if (scale <= 0)
    out += "0." + std::string(static_cast<std::size_t>(-scale), '0') + digits;
  else if (static_cast<std::size_t>(scale) >= digits.size())
    out += digits + std::string(static_cast<std::size_t>(scale) - digits.size(), '0');
  else
    out += digits.substr(0, static_cast<std::size_t>(scale)) + '.' + digits.substr(static_cast<std::size_t>(scale));
  o.text += out;
}

inline void Put(Out& o, bool v) { o.text += v ? "true" : "false"; }
inline void Put(Out& o, const std::string& v) { PutString(o, v); }
inline void Put(Out& o, const Time& v) { o.text += '"' + FormatTime(v) + '"'; }   // (as it is: a time's "+" is not escaped)

template <typename T>
void Put(Out& o, const std::vector<T>& v)
{
  o.text += '[';
  for (std::size_t i = 0; i < v.size(); ++i)
  {
    if (i)
      o.text += ',';
    Put(o, v[i]);
  }
  o.text += ']';
}

inline void PutMap(Out& o, const Map& v)
{
  o.text += '{';
  for (std::size_t i = 0; i < v.size(); ++i)
  {
    if (i)
      o.text += ',';
    PutString(o, v[i].first);
    o.text += ':';
    Put(o, v[i].second);
  }
  o.text += '}';
}

// A member's name, after a comma unless it is the first.
inline void Key(Out& o, bool& first, std::string_view name)
{
  if (!first)
    o.text += ',';
  first = false;
  PutString(o, name);
  o.text += ':';
}

// C#'s int arithmetic, which wraps.
inline std::int32_t Wrap(std::int64_t v) { return static_cast<std::int32_t>(static_cast<std::uint32_t>(v)); }
}  // namespace fzgx::json_wire
