# Repository instructions for AI coding assistants

## Owner-directed development workflow (highest priority for this repository)

This repository is the user's single active UCR project. **For user-requested implementation and fixes, work directly on the existing `master` branch.** Do not create feature branches, draft pull requests, review-only changes, or copies as an alternative to updating the program.

- Read and follow the owner's exact feature requirements. Implement them in `master`, using small, traceable commits.
- Run the available build/tests and correct failures. Clearly distinguish tested behaviour from unverified GUI interactions.
- The user tests changes through the actual program built from `master`; merely opening a pull request is **not** delivery.
- Preserve unrelated code, existing profiles, configuration, device mappings and work. Do not force-push, reset history, or overwrite concurrent edits.
- Check the current `master` head before changes and reconcile concurrent work as necessary.
- No branch or PR unless the owner **explicitly asks for one**. Do not assume conventional GitHub/GitFlow collaboration requirements override this instruction.
- Keep explanations concise and focus on delivering the change rather than making the owner perform debugging or QA.
- Older instructions in `CONTRIBUTING.md` describing forks, GitFlow and mandatory pull requests are historical upstream contributor guidance, **not** the owner's workflow for this repository.

When unable to safely deliver directly to `master`, explain the specific blocker; never silently substitute a PR or branch.
