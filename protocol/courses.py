"""The courses' names (courses.toml), loaded and checked. generate.py hands the result to courses_cs.py (Online.Protocol's
CourseTable) and courses_cxx.py (the game's fzgx::courses). A rule the file breaks is a SchemaError that names it.
Standard library only."""
import os
import re
import tomllib

here = os.path.dirname(os.path.abspath(__file__))
SCHEMA = os.path.join(here, "courses.toml")

TOP_KEYS = {"longest", "story", "course"}
STORY_KEYS = {"first", "chapters", "chapter"}
COURSE_KEYS = {"course", "place", "track"}
CHAPTER_KEYS = {"chapter", "track"}
# what the game's 24 by 24 font writes of a name: its own course names use letters, digits, the space and - ' . &
NAME = re.compile(r"[A-Za-z0-9](?:[A-Za-z0-9 '.&-]*[A-Za-z0-9])?\Z")
STORY_PLACE = "Story"


class SchemaError(Exception):
    pass


def whole(value, what, low, high):
    if type(value) is not int or not low <= value <= high:
        raise SchemaError(f"{what}: a whole number {low} to {high} (got {value!r})")
    return value


def name(value, what, longest):
    if not isinstance(value, str) or not NAME.match(value) or "  " in value:
        raise SchemaError(f"{what}: a name of letters, digits, single spaces and - ' . & (got {value!r})")
    if len(value) > longest:
        raise SchemaError(f"{what}: {value!r} is longer than {longest} characters")
    return value


def load(path=SCHEMA):
    """{longest, first, chapters, courses: [(course, place, track)] by course, chapters_named: [(chapter, course,
    track)] by chapter}, checked."""
    try:
        with open(path, "rb") as f:
            raw = tomllib.load(f)
    except tomllib.TOMLDecodeError as e:
        raise SchemaError(f"{os.path.basename(path)}: {e}") from None
    if set(raw) - TOP_KEYS:
        raise SchemaError(f"unknown keys {sorted(set(raw) - TOP_KEYS)}")
    longest = whole(raw.get("longest"), "longest", 1, 63)
    story = raw.get("story", {})
    if not isinstance(story, dict) or set(story) - STORY_KEYS:
        raise SchemaError(f"story: a table of {sorted(STORY_KEYS)}")
    first = whole(story.get("first"), "story.first", 1, 255)
    chapters = whole(story.get("chapters"), "story.chapters", 1, 255 - first + 1)
    courses, seen = [], set()
    for raw_course in raw.get("course", []):
        if set(raw_course) - COURSE_KEYS:
            raise SchemaError(f"course {raw_course.get('course', '?')}: unknown keys {sorted(set(raw_course) - COURSE_KEYS)}")
        course = whole(raw_course.get("course"), "course", 1, first - 1)
        if course in seen:
            raise SchemaError(f"course {course}: named twice")
        seen.add(course)
        place = name(raw_course.get("place"), f"course {course}: place", longest)
        if place == STORY_PLACE:
            raise SchemaError(f"course {course}: {STORY_PLACE!r} is the story's stages' place, not a venue")
        courses.append((course, place, name(raw_course.get("track"), f"course {course}: track", longest)))
    if [c[0] for c in courses] != sorted(c[0] for c in courses):
        raise SchemaError("the courses stand in the order of their numbers")
    named = []
    for raw_chapter in story.get("chapter", []):
        if set(raw_chapter) - CHAPTER_KEYS:
            raise SchemaError(f"chapter {raw_chapter.get('chapter', '?')}: unknown keys "
                              f"{sorted(set(raw_chapter) - CHAPTER_KEYS)}")
        chapter = whole(raw_chapter.get("chapter"), "story.chapter", 1, chapters)
        if any(c[0] == chapter for c in named):
            raise SchemaError(f"chapter {chapter}: named twice")
        named.append((chapter, first + chapter - 1, name(raw_chapter.get("track"), f"chapter {chapter}: track", longest)))
    if [c[0] for c in named] != sorted(c[0] for c in named):
        raise SchemaError("the story's chapters stand in the order of their numbers")
    return {"longest": longest, "first": first, "chapters": chapters, "courses": courses, "chapters_named": named}
