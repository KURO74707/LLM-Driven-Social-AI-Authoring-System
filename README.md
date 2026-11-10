# LLM-Driven Social AI Authoring System

Unity project snapshot exported from Perforce changelist **125012**.

- Unity Editor: **6000.5.10f1**
- Main integration scene: `Assets/Scenes/Cai_W08/Cai_W10.unity`
- Includes Alex/Sam group chat, emotional states and relationships, the AI Director, and physical Push / Avoid / Attack / Idle behaviors.

## Open the project

Add this repository folder in Unity Hub and open it with the editor version above. Unity regenerates its Library and temporary files locally.

NPC dialogue requires a running LM Studio server and the configured model (`zai-org/glm-4.6v-flash`). The exported scene uses `http://127.0.0.1:1235/v1/chat/completions`; update LLMClient in the Inspector if your local setup differs.

Perforce remains the original project's source control. This repository starts with a snapshot and does not contain the Perforce revision history. Unity asset metadata and GUIDs are preserved.