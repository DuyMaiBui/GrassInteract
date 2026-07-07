#!/usr/bin/env node
// t1k-origin: kit=theonekit-core | repo=The1Studio/theonekit-core | module=null | protected=true
'use strict';

/**
 * human-writing-reminder.cjs — UserPromptSubmit hook.
 *
 * Fires when the user's prompt asks to draft / write / reply / send an email,
 * message, or post. Injects a reminder so the t1k-human-writing skill is
 * activated BEFORE composing (it kept getting skipped because its keywords were
 * topic words like "humanize", not action phrases like "draft the email").
 *
 * Mechanism: print a `[t1k:human-writing]` line to stdout. UserPromptSubmit
 * hook stdout is surfaced to the model as context (same pattern as
 * check-module-keywords.cjs). Non-blocking; fail-open on error.
 */

try {
  let prompt = '';
  try { prompt = require('fs').readFileSync(0, 'utf8').toLowerCase(); } catch { /* no stdin */ }
  if (!prompt.trim()) process.exit(0);

  // Comms-composition intent: an action verb near a comms-artifact noun,
  // or an explicit "reply/respond to <someone>".
  const ACTION = '(draft|write|compose|rewrite|reword|reply|respond|send|word|phrase|polish|soften)';
  const ARTIFACT = '(e-?mail|message|msg|reply|response|post|note|dm|text|slack|whatsapp|discord|telegram|lark|feishu|linkedin|tweet|announcement|follow-?up)';

  // Tone / humanize rewrite intent: "make it more human", "more friendly",
  // "sound more natural", "less robotic", "less formal", "less like AI/ChatGPT",
  // "humanize this". These carry no action+artifact pair, so the matchers above
  // miss them — yet they ARE the skill's lint mode (rewrite an existing draft's
  // voice). This was the gap that let "make it more human, more friendly" slip
  // through and compose without the skill.
  // Exclude product/UI rewrites ("make the UI more friendly") — "friendly"/"natural"
  // there is about usability, not writing voice.
  const UI_CONTEXT = /\b(ui|ux|interface|button|screen|page|app|website|web ?page|layout|component|design system|onboarding flow)\b/.test(prompt);
  const HUMANIZE = !UI_CONTEXT && (
    /\b(more|less|too|sound|sounds|feels?|reads?|seems?)\b[^.?!]*\b(human|friendly|natural|casual|conversational|personal|warm|warmer|robotic|stiff|formal|ai|chatgpt|bot)\b/.test(prompt) ||
    /\b(humani[sz]e|de-?ai|less ?ai|not ?ai|sound human|read human|like a (robot|bot))\b/.test(prompt)
  );

  const intent =
    new RegExp(`\\b${ACTION}\\b[^.?!]*\\b${ARTIFACT}\\b`).test(prompt) ||
    new RegExp(`\\b${ARTIFACT}\\b[^.?!]*\\b${ACTION}\\b`).test(prompt) ||
    /\b(reply|respond|write|get) (back )?(to|him|her|them)\b/.test(prompt) ||
    /\bdraft (the|a|my|this|our)\b/.test(prompt) ||
    HUMANIZE;

  if (!intent) process.exit(0);

  console.log(
    '[t1k:human-writing] This prompt is about composing/sending an email, message, or post. ' +
    'BEFORE you draft or send, activate the t1k-human-writing skill (lint the body, or guide a fresh draft). ' +
    'Hard rule: remove EVERY em-dash (—); strip delve/leverage-class vocab, rule-of-three, hedging, ' +
    'sycophancy, and summary-conclusion closers. A single em-dash makes a business email read as AI-written.'
  );
  process.exit(0);
} catch {
  process.exit(0); // fail-open: never block a prompt on a reminder hook
}
