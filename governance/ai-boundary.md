# AI Boundary

`AiGovernance` is intentionally present as a boundary and intentionally absent as an execution surface.

## Allowed In Engineering

- AI policy metadata
- Governance evidence metadata
- Audit records for AI-related policy decisions
- Commercial capability checks for future AI features
- OpenAPI and UI surfaces that report the boundary state

## Not Allowed In Engineering

- Model deployment
- Prompt execution
- Agent execution
- Tool execution
- Provider selection
- Evaluation or fine-tuning jobs
- Production AI traffic

AI execution authority remains outside Engineering for this artifact.
