---
name: xapper
description: Use when something has to be checked in a running WPF desktop application rather than in code or tests - launching the app, clicking and typing in it, reading what a screen holds, or confirming that a change behaves as intended. Also "방금 작성한 코드 잘 돌아가는지 실제로 확인해줘", "앱 띄워서 확인해줘", "화면에서 직접 눌러봐", "UI 동작 확인해줘", "실제로 되는지 테스트해줘".
---

# Driving a WPF app with Xapper

Xapper injects into the target process and walks its real visual tree, so it sees controls that
accessibility-based tooling cannot. That power is wasted if you drive it like a screenshot robot.
Almost every wasted token in a Xapper session comes from one of two habits: taking a picture of
something the tree already knows, and re-learning a screen you have already understood.

Work in this loop, once per screen:

**arrive → recall → act → verify → record**

## 1. Arrive

Start the app yourself with `xapper_launch` whenever you can. It loads the inspector before the
app's entry point, so there is no injection step and screens that appear before the main window -
splash, login - are reachable too. Fall back to `xapper_list_processes` and `xapper_attach` only
when the app is already running.

## 2. Recall before you look

Call `xapper_screen_recall` **every time the screen changes**, not once at the start of the
session. Opening a document, switching a tab, moving to another step of a wizard, closing a dialog:
each is a new screen with its own record.

A known screen comes back with the selectors to act on, and you can work without looking at all.
An unknown screen is the signal to look once, and only once.

A known screen is also the moment to stop moving one step at a time. You already hold every
selector the screen offers, so plan the whole sequence up front - click this, type that, press
Enter - and send it in a single call instead of looking between each step. Build those steps from
the record's selectors rather than from refs: a selector still resolves on a later visit and in a
later session, while a ref does not survive the next snapshot. That is what a learned screen buys
you, and it is worth more than the picture you saved.

## 3. Act, and read the response

Every action reports which path it took and what happened. Read that instead of taking a picture to
find out.

**Click never refuses, so its word alone means little.** It tries an accessibility pattern, then a
button click event, and failing both it raises simulated routed mouse events at any UIElement -
reporting success either way. On a control that offers nothing, click is the action that still does
something. Read the path named in the response rather than trusting the word "Clicked". When the
element needs genuine input - it hit-tests, captures the mouse, or handles `MouseDown` rather than
`MouseLeftButtonDown` - pass `x`/`y` so real mouse messages are delivered.

**Five actions can refuse outright.** `type`, `toggle`, `expand`, `select` and `scroll` each try an
accessibility pattern and one stock WPF type, and say the element does not support the action when
neither fits. A custom control that looks like a text box may well not be one; when `type` refuses,
click into it with `x`/`y` and send `xapper_key` instead.

**The cursor usually stays the person's.** Coordinate clicks and most drags run inside the target
process: real window messages with the OS cursor reads briefly spoofed, so WPF accepts them as
genuine while the physical pointer never moves. The person can keep working. Only when that path
cannot be set up does Xapper fall back to real mouse input, which does move the pointer and take
focus - and the response says which one ran. When you expect the real one, call
`xapper_notice_show` first so a banner warns the person off, and `xapper_notice_hide` when that
stretch is done.

When an action refuses, the message names the single condition that blocked it:

| What it says | What it means | What to do |
|---|---|---|
| `stayed disabled` | the app itself refuses the action | a longer timeout will not help; find what enables the control, or accept that the action is unavailable here |
| `stayed hidden` | it is not on screen | open the tab, pane or dialog holding it first |
| `was still loading` | the view has not settled | raise the timeout, or act later |
| `cannot be resolved` | the ref is stale, or its element left the tree as virtualized rows and closed dialogs do | take fresh refs |
| `matches N elements` | the selector is ambiguous | add a second clause, or pass a ref |

**A ref outlives its meaning, which is worse than failing.** `xapper_snapshot` does not merely
invalidate earlier refs - it restarts numbering from 1, so a ref taken before a snapshot may now
resolve to a *different* element and act on it without complaint. Use refs from the most recent
snapshot, and prefer target selectors when a sequence spans one. `xapper_find` leaves existing refs
alone and only hands out new numbers.

Send a sequence as one call rather than one tool call per step, and check the result once at the
end rather than between steps. Two tools do this:

- **`xapper_batch`** for a straight line of steps. They run in order, the batch stops at the first
  failure, and you get one numbered result per step in the format the single tools return. Steps
  take target selectors, so a whole flow runs without a snapshot in between. A step that opens a
  modal dialog ends the batch, and the remaining steps do not run.
- **`xapper_run`** when there is a loop, a condition or a wait. The script runs entirely inside the
  app and returns a summary. Prefer `waitUntil` over sleeping, so the same script can be re-run.

## 4. Verify without pixels

Choose by what you actually need to know:

- **Did the action work?** The action's own response already said so.
- **What is this one value now?** `xapper_get_property` or `xapper_assert`.
- **What does this panel hold?** `xapper_snapshot` with `rootRef` set to it. Same content, as text.
- **How is it drawn?** Only then a picture: layout, rendering, a chart, a control that paints itself.

When you do take one, narrow it with `ref`. That works in both modes, so choosing `mode="screen"`
to catch a dialog does not force a full-desktop shot. Only `annotate` is render-only.

Some controls genuinely cannot be read as text. High-performance grids - DevExpress `GridControl`
and `TreeList` among them - paint their cells without creating a visual element per cell, so their
contents appear in neither a snapshot nor `xapper_find`. Pixels are the only way in. Note that fact
on the screen record the first time you meet it, so the next session does not rediscover it.

## 5. Record what you learned

This is the step that gets skipped, and it is the one that pays.

**`xapper_screen_learn` once per screen.** Do it immediately after you have looked at an unknown
screen and understood it. You supply only a one-line name and any gotcha; Xapper works out the
regions itself from the live visual tree. From then on, `xapper_screen_recall` hands you those
selectors for a fraction of what a picture costs, in this session and in every later one - and
those selectors are what let you batch a whole flow instead of stepping through it.

**`xapper_screen_note` whenever you find something out.** Most of what is worth remembering is not
visible on arrival - it surfaces when you act:

- what a disabled button is waiting for
- a control that refuses typed text, and what works instead
- a dialog that lives outside the visual tree and needs real mouse input
- which grid paints its cells and therefore needs a picture
- where a button actually leads

One line each. The note is appended, so earlier notes survive, and the regions are left untouched.

**Every screenshot response tells you where you stand.** It says either that this screen is already
learned - in which case recall its selectors rather than looking again - or that it is not in the
record yet, which means the moment you finish looking is the moment to learn it. Act on that line;
it is why it is there.

Two properties make the record durable, and both are worth knowing:

- The key is the screen's **structure**, with text and data deliberately excluded. Typing in a box,
  selecting a row or scrolling a virtualized list does not change it, so a record keeps matching as
  data changes.
- A rebuilt app produces new structures and therefore new keys, so stale records stop matching
  rather than answering wrongly. Use `xapper_screen_forget` after a redesign you want re-learned.

## What the measurements show

These are the habits that actually cost sessions, each seen in a real transcript:

- 46 screenshots taken while `xapper_screen_learn` was never called once, so the next session
  started from nothing again.
- Recall called a single time, right after launch, while the app still showed its splash screen -
  and never again, so a good record built on the previous run was never opened.
- 35 of 46 pictures taken purely to check whether an action had worked.
- Nine consecutive full-desktop captures at maximum width, when a `ref` would have framed the one
  panel in question.
- A disabled button clicked three times, each costing the full timeout, each followed by a property
  read asking why.
