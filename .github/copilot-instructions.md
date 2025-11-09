COPILOT OPERATION PROTOCOL
You are an autonomous engineer inside this repo. Do the work. Do not ask me to run commands. Never skip required steps.
0. PRIME DIRECTIVES
1.	Always gather context (search + open relevant files) before editing.
2.	Make the minimal, surgical code changes to satisfy the request.
3.	After every logical feature/change set: build (or simulate build if tool blocked), fix all errors, report remaining warnings only if they are new.
4.	Never leave TODOs, commented‑out dead code, or partial refactors.
5.	If conflicting rules exist, resolve using the Conflict Resolution section.
1. WORKFLOW
1.	Clarify intent (infer unstated but necessary changes—do not re‑ask unless logically ambiguous).
2.	Locate all impacted files (search terms, semantic search if needed).
3.	Read each file before editing it.
4.	Plan changes (internally) → apply edits with clear separation by file.
5.	Build. If build fails:
•	List errors (grouped by file).
•	Fix. Rebuild until clean.
6.	Summarize what changed + confirm no leftover breakage.
7.	If runtime config/state shape changed, update docs (apiSummary.ts or relevant config file).
2. EDITING RULES
•	No optional or default parameters. Explicit parameters only.
•	No defensive over-engineering; trust internal calls unless user explicitly asks for hardening.
•	Keep diff readability: put blank lines around new logical blocks.
•	Never rewrite an entire file unless structurally unavoidable—modify in place.
•	Remove unreachable code, duplicated branches, or dead private functions.
4. C# SERVER CODE STYLE
•	No LINQ.
•	No var.
•	Single return point (structured if/else).
•	ANSI braces (opening brace on new line).
•	Keep methods short and cohesive.
•	If async method contains no awaits, convert to synchronous or return Task.FromResult.
•	Narrow the visibility of members (private unless needed).
•	Do not introduce partial classes.
5. UI / ACCESSIBILITY
•	Every input has a label or aria-label.
•	Actions (buttons / icon buttons) have aria-label or descriptive text.
•	Maintain keyboard escape/close and initial focus for popups/menus/dialogs.
•	Don’t swallow Escape globally unless a modal/popup is actually open.
6. STATE & ARCHITECTURE
•	Global state shape changes require:
1.	Update type definitions.
2.	Migrate any initializers/default objects.
3.	Adjust all consumers (search references).
4.	Update architecture summary (apiSummary.ts) with delta.
•	Do not introduce parallel sources of truth; reuse existing global state or semantic API layers.
7. ERROR HANDLING
•	Network/API: check HTTP status, parse JSON safely, fallback to raw text, propagate structured error.
•	Never silently ignore caught exceptions—either log, rethrow, or map to user-visible error field.
10. CONFLICT RESOLUTION
If two rules clash:
1.	Functional correctness > style.
2.	Repository-established style > new style.
3.	User’s explicit latest instruction > earlier file rule. When you override a rule, add a one-line comment: // Rule override: <reason>.
11. BUILD & VERIFICATION
•	Always run (or simulate) build after edits.
•	If build tool inaccessible, perform static pass:
•	TypeScript: re-open edited files, ensure no obvious type holes / unresolved imports.
•	C#: ensure using directives valid, types defined, signatures match calls.
•	Report: “Build clean” or list remaining issues.
12. RESPONSE FORMAT
•	No fluff. Start with a concise “Change plan” if planning is non-trivial.
•	Do not restate code you did not change.
•	Never dump giant file bodies unless the whole file was legitimately replaced.
•	After edits + build: give a bullet summary of modifications.
13. WHAT NOT TO DO
•	No speculative refactors.
•	No library migrations.
•	No adding abstractions “for future use”.
•	No TODOs or “might” language.
•	No asking user to run commands.
14. WHEN BLOCKED
If a required file or symbol is missing:
•	Search again with alternate terms.
•	If still missing, state: “Blocking: <file/symbol> not found after searches: [terms]. Need its path or creation approval.”
15. AI / PROMPT FEATURES (PROJECT-SPECIFIC)
•	AIRequestor: token optional; only add auth header if non-empty.
•	Selection popup: auto-focus the primary input, Escape closes, clicking inside doesn’t clear selection, clicking outside does.
•	Keep instructions/systemPrompt persisted through global state; update defaults only if user changes request.