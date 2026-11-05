# Independent quality review: what is working, what still needs activation

## Mechanism

UCR already runs Windows builds, NUnit tests, actual application startup, and captures 16 WPF screenshots. The independent quality-review workflow waits for a successful Windows build on master, downloads the screenshots from that exact run, checks out that exact commit, and asks separate AI reviewers to assess visual coherence and functional regression risks. Reviewers do not share the developer's conversation or self-evaluation. Missing evidence or errors cause NOT APPROVED.

The reviewer produces verdict.json and review.md and the GitHub job fails on rejection.

## One-time credential required

1. Create an OpenAI project API key with a spending limit in the OpenAI Platform. API charges are separate from a ChatGPT subscription.
2. At https://github.com/Stylish-Dark/UCR/settings/secrets/actions add a repository secret named OPENAI_API_KEY. Do not put keys into code or chat.

The workflow fails closed until a key is configured. Default models: gpt-6.1-terra for screenshot review and gpt-6.1-sol for functional-risk review.

## Important remaining enforcement limitation

This is presently an after-push gate, not a protected pre-merge gate. Evaluations after pushing to master cannot prevent the push. The ChatGPT GitHub connector cannot administer branch protection/rulesets. The current AGENTS.md explicitly requires direct master changes. Therefore this setup provides truly independent reviews, but not yet an unbypassable release boundary.

To enforce review before merging, future changes must use staging branches and master must require both a PR-triggered Windows build and an independently issued required status. Protect master against direct pushes, including implementers, and update AGENTS.md once that system is live. The present post-push workflow is NOT a suitable required pre-merge check.

## Known evidence limitations

- Rendered WPF screenshots do not cover every open menu, hover, resolution, scaling, physical device or input-capture interaction.
- Functional-risk review is not execution; the existing Windows NUnit/startup checks remain necessary and require targeted expansion.
- AI reviewers can miss defects or raise false positives. A separate review helps but cannot guarantee taste.
- This does not automatically remediate rejections or govern the developer's context budget; those need a running orchestrator with restricted write permissions.

## Where to see it

After a Windows build, see Independent UCR Quality Review under https://github.com/Stylish-Dark/UCR/actions and inspect UCR-Independent-Review artifacts and the exact SHA.
