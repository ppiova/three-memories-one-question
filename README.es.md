# Tres memorias, una pregunta

*[Read in English](README.md)*

¿Qué hace un agente de IA con algo que le contaste hace tres meses?

Este es un experimento chico que podés correr en tu máquina, con Docker y sin API keys. Una persona le cuenta 49 cosas a un asistente a lo largo de tres meses. Tres tipos de memoria de agente escuchan todo:

| Memoria | Dónde guarda lo que escucha | Qué pone en el prompt cuando preguntás |
|---|---|---|
| **file** | una línea en `MEMORY.md` | el archivo desde arriba, hasta que se acaba el lugar |
| **vector** | un embedding por frase | las frases que más se parecen a la pregunta |
| **graph** | personas, eventos, hechos, fechas y relaciones en Neo4j, escritos por [AgentMemory for .NET](https://github.com/joslat/agent-memory-dotnet) | los hechos que más se parecen a la pregunta, más las relaciones que los rodean |

Después, en una conversación nueva, cada memoria recibe las mismas tres preguntas, el mismo modelo y el mismo lugar en el prompt. El programa muestra qué llegó al prompt, qué respondió el modelo y qué datos se perdieron sin que nadie avisara.

## Por qué lo hice

La sesión de Neo4j Live de Jose Luis Latorre, [AgentMemory for .NET: Persistent AI Agent Memory Engine](https://www.youtube.com/watch?v=yYPleJ635VY), tiene una slide que me quedó dando vueltas: una pregunta, tres memorias, y una respuesta equivocada que llega sin ningún error. Quise verlo pasar en mi máquina, con un ejemplo mío, usando su librería como memoria de grafo.

El resultado fue menos prolijo que la slide, y creo que por eso sirve más.

## Qué pasó

Sofía es tech lead. En septiembre cuenta que lidera una salida a producción, presencial en Córdoba, el 11 de diciembre. En noviembre comenta, como novedad personal, que va a estar en Bariloche del 9 al 13 de diciembre por el casamiento de su hermana. Las dos frases están en la misma memoria y nadie las pone nunca una al lado de la otra.

El 4 de diciembre pregunta tres cosas:

| | ¿Cuándo es la salida a producción de Acme, y dónde? | ¿Qué tengo entre el 7 y el 13 de diciembre? | Armame la preparación para la semana de la salida. |
|---|:-:|:-:|:-:|
| | *la respuesta se parece a la pregunta* | *la pregunta nombra un período* | *chocan dos datos que no se parecen* |
| **file** | ✓ | ✗ | ✗ |
| **vector** | ✓ | ✓ | ✗ |
| **graph** | ✗ | ✓ | ✗ |

✓ los datos llegaron al prompt y la respuesta es correcta · ✗ un dato nunca llegó al prompt, y nada lo avisó

Esta es la corrida de referencia: `gemma3:12b` y `bge-m3` en un Ollama local, AgentMemory 1.9.0 con su preset Conversational, Neo4j 5.26. La corrí dos veces y dio la misma tabla. Todos los prompts y todas las respuestas están en [docs/reference-run.md](docs/reference-run.md). El escenario y las respuestas están en inglés.

Para la tercera pregunta, las tres memorias produjeron un plan seguro de sí mismo:

> **file:** Okay, here's a plan for go-live week (December 8-11, 2026) for the Acme Logistics billing system…
>
> **vector:** Okay, here's a plan for go-live week, December 7-11, 2026, for the Acme Logistics billing system…
>
> **graph:** Okay, here's a plan for go-live week, focusing on your priorities…

Ninguno menciona que esa semana Sofía va a estar en un casamiento en otra ciudad. Eso es fallar en silencio: no es un error, es una respuesta prolija armada con lo que justo había en el prompt.

Lo que me llevo:

- **El archivo** acierta mientras el dato esté cerca del principio. El viaje se dijo en noviembre, a 785 tokens del inicio de un archivo de 1.049, y el corte estaba cerca de los 600.
- **El vector store** encuentra lo que se parece a la pregunta, fechas incluidas. Cuando le preguntaron por el 7 al 13 de diciembre, trajo el viaje. Cuando le pidieron planificar la semana de la salida, el viaje no se parece a la pregunta: queda tan abajo en el ranking que hacían falta 706 de los 1.049 tokens para alcanzarlo.
- **El grafo** guardó el viaje como uno querría: `user · will be in · Bariloche`, válido del 2026-12-09 al 2026-12-13, con las fechas como dato. Igual no lo trajo para la pregunta de planificación, porque ahí el recall también arranca por similitud y después sigue el grafo un salto desde lo que la similitud encontró. El [mapa de memoria](https://github.com/joslat/agent-memory-dotnet/blob/main/docs/memory-map.md) de la librería lo dice sin vueltas.
- **El grafo también falló la primera pregunta.** La fecha de la salida estaba guardada, pero no quedó entre los diez hechos que trajo el recall, y el modelo respondió "the go-live starts at 7 am".
- **Al escribir es donde una memoria de grafo pierde cosas.** El modelo que lee cada frase, acá uno local de 12B, guardó el casamiento como "December 3" en vez del 12 de diciembre y anotó que Sofía trabaja en Acme, que es su cliente. Ocho relaciones que extrajo no se guardaron porque uno de sus extremos no era una entidad. Un modelo más fuerte va a extraer mejor. Solo te enterás mirando.

Entonces la conclusión no es "usá un grafo", y tampoco "no lo uses". Es la frase con la que Jose cierra su charla: la memoria no debería fallar en silencio, así que hay que mirarla y medirla. Mudar la memoria a otro lado no te ahorra mirar qué llegó al prompt.

## Cómo correrlo

Necesitás Docker y el SDK de .NET 10. Con una GPU NVIDIA de 12 GB o más anda cómodo. Sin GPU corre en CPU, y eso no lo medí.

```bash
docker compose up -d
```

La primera vez baja Neo4j, Ollama y dos modelos (`bge-m3` y `gemma3:12b`): unos 20 GB en disco. Los modelos están listos cuando el contenedor `models` termina; `docker compose logs -f models` muestra la descarga.

Con GPU NVIDIA, usá este en su lugar:

```bash
docker compose -f compose.yaml -f compose.gpu.yaml up -d
```

Después corré el experimento:

```bash
dotnet run --project src/ThreeMemories
```

En una RTX 3080 Ti de notebook una corrida tarda unos cinco minutos, cuatro de ellos la memoria de grafo leyendo las 49 frases.

Cada corrida imprime la tabla y escribe tres archivos en `results/`:

| Archivo | Qué es |
|---|---|
| `latest.md` | todos los prompts, todas las respuestas y lo que guardó el grafo |
| `MEMORY.md` | el archivo de notas, tal como lo escribió la memoria de archivo |
| `graph-memory.log` | lo que la librería advirtió o salteó al escribir |

Para ver el grafo, abrí http://localhost:7474 (usuario `neo4j`, contraseña `password`) y corré la consulta que el programa imprime al final.

### Usar otros modelos

Los modelos los resuelve la capa de proveedores de AgentMemory, así que acá valen [sus variables de entorno](https://github.com/joslat/agent-memory-dotnet/blob/main/docs/configuration/inference-providers.md). Sin configurar nada, la demo usa el Ollama que `compose.yaml` levanta en el puerto 11435.

| Para usar | Configurá |
|---|---|
| un Ollama que ya tenés | `OLLAMA_ENDPOINT=http://127.0.0.1:11434/v1`, y levantá solo Neo4j con `docker compose up -d neo4j` |
| otro modelo local | `OLLAMA_MODEL`, tanto para `docker compose up` como para `dotnet run` |
| un modelo hosteado | `AI_INFERENCE_PROVIDER` y las variables de ese proveedor, como en el enlace de arriba |

Solo lo corrí con los modelos locales de arriba. Si cambiás el modelo de embeddings, empezá con un volumen de Neo4j vacío: el índice vectorial se crea para un único ancho de embedding.

## Cómo se mantiene parejo

- **Las mismas frases.** Cada memoria escucha las mismas 49 frases en el mismo orden. Un reloj guionado reproduce los tres meses en minutos, así cada frase queda con la fecha del día en que se dijo. AgentMemory lee la hora de un reloj inyectable, y eso lo hace posible.
- **El mismo modelo.** Un único modelo responde todas las preguntas con las mismas instrucciones. Solo cambia el bloque de memoria.
- **El mismo lugar.** La memoria de grafo decide cuánto trae (acá, entre 569 y 632 tokens). El archivo y el vector store reciben exactamente esa cantidad de tokens.
- **Nada ajustado a mano.** La memoria de grafo está configurada como el sample ConversationalAgent de la propia librería: el preset Conversational en cada paquete. El único cambio es que las frases se aprenden en línea y no en segundo plano, para que cada una quede con la hora del guion.
- **Dos tipos de chequeo.** "Un dato llegó al prompt" es una búsqueda de texto, así que es exacto. "La respuesta es correcta" lo califica el mismo modelo con una pregunta de sí o no, así que puede equivocarse. Por eso el reporte imprime todas las respuestas.

Los tokens son estimados: caracteres dividido cuatro.

## Qué no es

- **No es un benchmark.** Es un escenario, tres preguntas y un modelo local chico. Para números medidos de la librería, mirá su propio trabajo de evaluación en el [repositorio de AgentMemory](https://github.com/joslat/agent-memory-dotnet).
- **No es tamaño real.** 49 frases representan meses de uso real. A este tamaño toda la historia entra en cualquier ventana de contexto, y un archivo cargado entero acertaría todas las preguntas. El experimento es sobre el día en que ya no entra.
- **No son la mejor memoria de archivo ni la mejor vectorial.** Son las versiones honestas más simples: un archivo leído desde arriba y similitud coseno sobre las frases crudas. Las reales suman resúmenes, reranking y filtros por metadatos.
- **No es independiente del modelo.** Lo que queda en el grafo depende del modelo que lee cada frase.

## Hacelo tuyo

El escenario es un único JSON, [scenarios/go-live.json](scenarios/go-live.json): sesiones con una fecha y lo que se dijo, y después las preguntas. Cada pregunta lista los datos que el prompt tiene que contener y qué dice una respuesta correcta.

```bash
dotnet run --project src/ThreeMemories -- --scenario ruta/al/tuyo.json
dotnet run --project src/ThreeMemories -- --only file,vector   # sin el grafo; no hace falta Neo4j
```

El código es chico a propósito:

| Archivo | Qué tiene |
|---|---|
| [Memories/FileMemory.cs](src/ThreeMemories/Memories/FileMemory.cs) | el archivo de notas |
| [Memories/VectorMemory.cs](src/ThreeMemories/Memories/VectorMemory.cs) | embeddings y similitud coseno |
| [Memories/GraphMemory.cs](src/ThreeMemories/Memories/GraphMemory.cs) | AgentMemory for .NET, y una consulta que muestra lo que guardó |
| [Experiment.cs](src/ThreeMemories/Experiment.cs) | la escritura y las preguntas |
| [Report.cs](src/ThreeMemories/Report.cs) | el reporte en Markdown |

## Créditos

La memoria de grafo es [AgentMemory for .NET](https://github.com/joslat/agent-memory-dotnet), de Jose Luis Latorre Millas, con licencia MIT. La idea de hacerle una misma pregunta a tres memorias sale de su charla, enlazada arriba. Si esto te sirve, el repositorio al que hay que darle la estrella es el suyo.

Esta demo es mía, y sus errores también. No está afiliada a Neo4j ni al proyecto AgentMemory.

## Licencia

[MIT](LICENSE)
