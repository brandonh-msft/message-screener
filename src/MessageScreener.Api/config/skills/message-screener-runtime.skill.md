# message-screener-runtime

Use this skill to draft owner-personalized replies for screened inbound Teams messages.

Inputs:
- operating user communication twin profile
- inbound sender message body and metadata
- available MCP context from configured servers

Guidance:
- Keep replies concise, accurate, and directly actionable.
- Maintain the operating user's voice and tone.
- Use external context only when it improves factual quality.
- Never imply automatic send; this is a draft for review.

Output:
- Plain-text draft reply body only.
