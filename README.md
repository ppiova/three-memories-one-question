# Three memories, one question

*[Leer en español](README.es.md)*

What does an AI agent do with something you told it three months ago?

This is a small experiment you can run on your own machine with one Docker command and no API keys. One person tells an assistant 49 things over three months. Three kinds of agent memory hear all of it:

| Memory | Where it keeps what it hears | What it puts in the prompt when you ask |
|---|---|---|
| **file** | a line in `MEMORY.md` | the file from the top, until the room runs out |
| **vector** | one embedding per statement | the statements that most resemble the question |
| **graph** | people, events, facts, dates and relationships in Neo4j, written by [AgentMemory for .NET](https://github.com/joslat/agent-memory-dotnet) | the facts that most resemble the question, plus the relationships around them |

Later, in a new conversation, each memory gets the same three questions, the same model and the same room in the prompt. The program shows what reached the prompt, what the model answered, and which facts were lost without anyone being told.

## Why I built it

Jose Luis Latorre's Neo4j Live session, [AgentMemory for .NET: Persistent AI Agent Memory Engine](https://www.youtube.com/watch?v=yYPleJ635VY), has a slide I kept thinking about (he also presented the library in Spanish at [.NET Baires](https://www.youtube.com/watch?v=avSeR85brrY) on October 22, 2026): one question, three memories, and a wrong answer that arrives without any error. I wanted to watch that happen on my own machine, with my own example, using his library as the graph memory.

The result was less tidy than the slide, and I think that makes it more useful.

## What happened

Sofía is a tech lead. In September she says she is leading a go-live, on-site in Córdoba, on December 11. In November she mentions, as personal news, that she will be in Bariloche from December 9 to 13 for her sister's wedding. Both statements are in the same memory and nobody ever puts them side by side.

On December 4 she asks three things:

| | When is the Acme go-live, and where? | What do I have between December 7 and 13? | Plan my prep for go-live week. |
|---|:-:|:-:|:-:|
| | *the answer resembles the question* | *the question names a period* | *two facts that don't look alike collide* |
| **file** | ✓ | ✗ | ✗ |
| **vector** | ✓ | ✓ | ✗ |
| **graph** | ✗ | ✓ | ✗ |

✓ the facts reached the prompt and the answer is right · ✗ a fact never reached the prompt, and nothing said so

This is the reference run: `gemma3:12b` and `bge-m3` on a local Ollama, AgentMemory 1.9.0 with its Conversational preset, Neo4j 5.26. I ran it three times, one of them inside Docker, and got the same table every time. Every prompt and every answer is in [docs/reference-run.md](docs/reference-run.md).

For the third question, all three memories produced a confident plan:

> **file:** Okay, here's a plan for go-live week (December 8-11, 2026) for the Acme Logistics billing system…
>
> **vector:** Okay, here's a plan for go-live week, December 7-11, 2026, for the Acme Logistics billing system…
>
> **graph:** Okay, here's a plan for go-live week, focusing on your priorities…

None of them mentions that Sofía will be at a wedding in another city that week. That is what failing silently looks like: not an error, but a good-looking answer built on whatever happened to be in the prompt.

What I take from it:

- **The file** is right while the fact sits near the top. The trip was said in November, 785 tokens into a 1,049-token file, and the cut was at about 600.
- **The vector store** finds what resembles the question, dates included. Asked about December 7 to 13, it brought the trip. Asked to plan go-live week, the trip does not resemble the question: it ranks so low that 706 of the 1,049 tokens would have been needed to reach it.
- **The graph** stored the trip the way you would want it: `user · will be in · Bariloche`, valid from 2026-12-09 to 2026-12-13, with the dates as data. It still did not bring it back for the planning question, because recall starts from similarity there too, and then follows the graph one hop from what similarity found. The library's [memory map](https://github.com/joslat/agent-memory-dotnet/blob/v1.9.0/docs/memory-map.md) says so in plain words.
- **The graph also missed the first question.** The go-live date was stored, but it was not among the ten facts recall brought back, and the model answered "the go-live starts at 7 am".
- **Writing is where a graph memory loses things.** The model that reads each statement, a local 12B one here, stored the wedding as "December 3" instead of December 12 and wrote that Sofía works at Acme, her client. Eight relationships it extracted were not stored because one of their ends was not an entity. A stronger model will extract better. You only find out by looking.

So the conclusion is not "use a graph", and it is not "don't". It is the line Jose closes his talk with: memory shouldn't fail silently, so watch it and measure it. Moving the memory somewhere else does not spare you from looking at what reached the prompt.

## Run it

You need Docker. An NVIDIA GPU with 12 GB or more makes it comfortable. Without one it runs on CPU, and I have not timed that.

```bash
docker compose run --rm demo
```

That one command starts Neo4j and Ollama, downloads two models (`bge-m3` and `gemma3:12b`) and runs the experiment. The first time it needs about 20 GB of disk and a while to download.

With an NVIDIA GPU, use this instead:

```bash
docker compose -f compose.yaml -f compose.gpu.yaml run --rm demo
```

On a laptop RTX 3080 Ti a run takes about five minutes, four of them the graph memory reading the 49 statements.

A run prints the scoreboard and writes three files to `results/`:

| File | What it is |
|---|---|
| `latest.md` | every prompt, every answer, and what the graph stored |
| `MEMORY.md` | the notes file, as the file memory wrote it |
| `graph-memory.log` | what the library warned about or skipped while writing |

To see the graph, open http://localhost:7474 (user `neo4j`, password `password`) and run the query the program prints at the end.

When you are done, `docker compose down` stops everything and keeps the downloaded models.

### The image

The experiment's image is `ghcr.io/ppiova/three-memories-one-question`, built for amd64 and arm64 by the [workflow](.github/workflows/image.yml) in this repository. Compose pulls it, and builds it from the [Dockerfile](Dockerfile) if it cannot.

The image runs as a non-root user. I ran it on Docker Desktop for Windows. On Linux, if the run cannot write its report, make the folder writable first with `mkdir -p results && chmod o+w results`.

### With the .NET SDK

To change the code and run it directly, you need the .NET 10 SDK:

```bash
docker compose up -d
dotnet run --project src/ThreeMemories
```

### Using other models

Models are resolved by AgentMemory's own provider layer, so [its environment variables](https://github.com/joslat/agent-memory-dotnet/blob/v1.9.0/docs/configuration/inference-providers.md) work here. With nothing set, the demo uses the Ollama that `compose.yaml` starts on port 11435.

| To use | Set |
|---|---|
| an Ollama you already have | `OLLAMA_ENDPOINT=http://127.0.0.1:11434/v1`, start only Neo4j with `docker compose up -d neo4j`, and run with `dotnet run` |
| another local model | `OLLAMA_MODEL`, for Compose and, if you use it, for `dotnet run` |
| a hosted model | `AI_INFERENCE_PROVIDER` and that provider's variables, as in the link above, and run with `dotnet run` |

I have only run it with the local models above. If you change the embedding model, start from an empty Neo4j volume: the vector index is built for one embedding width.

## How it is kept fair

- **Same statements.** Every memory hears the same 49 statements in the same order. A scripted clock replays the three months in minutes, so each statement is stamped with the day it was said. AgentMemory reads time from an injectable clock, which makes that possible.
- **Same model.** One model answers every question with the same instructions. Only the memory block changes.
- **Same room.** The graph memory decides how much it brings back (569 to 632 tokens here). The file and the vector store then get exactly that many tokens.
- **Nothing tuned.** The graph memory is configured like the library's own ConversationalAgent sample: the Conversational preset on each package. The one change is that statements are learned inline instead of in the background, so that each gets the scripted time.
- **Two kinds of check.** "A fact reached the prompt" is a text search, so it is exact. "The answer is right" is graded by the same model with a yes/no question, so it can be wrong. That is why the report prints every answer.

Token counts are estimates: characters divided by four.

## What this is not

- **Not a benchmark.** It is one scenario, three questions and one small local model. For measured numbers on the library, see its own evaluation work in the [AgentMemory repository](https://github.com/joslat/agent-memory-dotnet).
- **Not full size.** 49 statements stand in for months of real use. At this size the whole history fits in any context window, and a file loaded whole would pass every question. The experiment is about the day it no longer fits.
- **Not the best file or vector memory.** They are the simplest honest versions: a file read from the top, and cosine similarity over the raw statements. Real ones add summaries, reranking and metadata filters.
- **Not independent of the model.** What the graph holds depends on the model that reads each statement.
- **Not the latest code.** It was measured with AgentMemory 1.9.0, still the latest on NuGet on October 22, 2026. The library's main branch already has unreleased, opt-in changes to how memories are written. When a new version ships I will run it again.

## Make it yours

The scenario is one JSON file, [scenarios/go-live.json](scenarios/go-live.json): sessions with a date and what was said, then questions. Each question lists the facts the prompt must contain and what a right answer says.

```bash
dotnet run --project src/ThreeMemories -- --scenario path/to/yours.json
dotnet run --project src/ThreeMemories -- --only file,vector   # skip the graph; no Neo4j needed
docker compose run --rm demo --only file,vector               # the same, inside Docker
```

The code is small on purpose:

| File | What it holds |
|---|---|
| [Memories/FileMemory.cs](src/ThreeMemories/Memories/FileMemory.cs) | the notes file |
| [Memories/VectorMemory.cs](src/ThreeMemories/Memories/VectorMemory.cs) | embeddings and cosine similarity |
| [Memories/GraphMemory.cs](src/ThreeMemories/Memories/GraphMemory.cs) | AgentMemory for .NET, and a query that shows what it stored |
| [Experiment.cs](src/ThreeMemories/Experiment.cs) | the writing and the asking |
| [Report.cs](src/ThreeMemories/Report.cs) | the Markdown report |

## Credits

The graph memory is [AgentMemory for .NET](https://github.com/joslat/agent-memory-dotnet) by Jose Luis Latorre Millas, MIT licensed. The idea of asking one question to three memories comes from his talk, linked above. If this is useful to you, his repository is the one to star.

This demo is mine, and so are its mistakes. It is not affiliated with Neo4j or with the AgentMemory project.

## License

[MIT](LICENSE)
