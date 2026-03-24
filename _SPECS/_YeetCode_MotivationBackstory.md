# Welcome to YeetCode — and why it exists

Hey! I'm David, the author of YeetCode. I want to share some background on where this project comes from, because the design choices make a lot more sense with context.

## The short version

YeetCode is a schema-driven code generation pipeline. You define a data shape, feed it data, and run it through templates to produce output files. Every stage is a human-readable file you can inspect, edit, and diff independently.

It comes in two modes:

**HalfYeet** — data-driven templating. You hand-author or programmatically produce HJSON data, validate it against a schema, and run it through templates:

```
data (HJSON/JSON/C#) ──→ template ──→ output files
         ↑
      schema
     (validates)
```

**FullYeet** — language-to-language transformation. You write a PEG grammar that parses an input syntax directly into the validated data, then template as before:

```
input file ──→ grammar ──→ data (HJSON) ──→ template ──→ output files
                              ↑
                           schema
                          (validates)
```

## Where it comes from: Clearsilver and data-driven templating

Back around 2000, I was working on the merger of eGroups.com and Onelist.com. Onelist had a templating system called CS/HDF, designed by Scott Shambarger and a colleague. They'd built it because they were writing web pages primarily in C — they needed somewhere to put the data for their templates, so they created HDF, a simple hierarchical data format. The template engine (CS) could only read from HDF. It couldn't call functions, hit databases, or reach back into the application.

At eGroups we'd been using a lot of Python, and when we saw CS/HDF, we immediately recognized something powerful: because the data layer was just serialized strings in a hierarchy, *any language* could populate it. We built the merged site with C for performance-critical pages and Python for everything else, all sharing the same templates. The templates didn't know or care which language generated the data.

Brandon Long later did a clean-room reimplementation called [Clearsilver](http://clearsilver.net), which we open-sourced at our startup Neotonic. I went on to use it at Google for Orkut and other projects. Other Googlers reimplemented it as J-Silver (pure Java Clearsilver).

The key architectural insight — which honestly emerged accidentally from C's constraints rather than from grand theory — was that **serializing data through an inert intermediate format is a stronger MVC separation than any API convention can provide**. The template literally *cannot* issue backend calls because there are no live objects to call methods on. There's just data.

YeetCode brings that same philosophy into the modern era. Instead of HDF (which was its own format), the intermediate data is plain HJSON — easy to read, easy to edit, easy to diff, and familiar to anyone who's seen JSON. You can also feed it data from C# objects, regular JSON, or the YTSON schema format. The core principle is the same: the template sees only inert data, never live objects.

## How this relates to StringTemplate

If you're familiar with Terence Parr's StringTemplate (often paired with ANTLR), you'll notice a similar philosophy around separating model from view. Parr wrote a formal paper arguing that templates should be restricted to prevent side effects.

Clearsilver actually predated StringTemplate by a few years, and arrived at the same destination from a different direction. Where StringTemplate enforces separation by restricting what *operations* the template language allows (no method calls, no assignment), Clearsilver/YeetCode enforces it by restricting what *data* the template can see. The template operates on already-materialized, inert data — strings and numbers in a hierarchy. There are no objects whose property getters could secretly trigger computation.

In practice, this turns out to be the stronger guarantee. In any dynamic language, a "property access" on an object passed to StringTemplate could trigger arbitrary code via `__getattr__`, Proxy objects, or lazy-init getters. StringTemplate can't know. With a serialized data intermediate, side effects are structurally impossible — not by convention, but by construction.

That said, Clearsilver and YeetCode templates aren't afraid of having *some* logic. Loops, conditionals, and simple expressions are fine — the goal isn't "zero logic in templates" (which leads to contortions in the data layer), it's "the template can't reach outside its sandbox." The logic stays limited to presentation concerns: iterating over collections, choosing between display variants, formatting values.

## Going further: custom grammars and language-to-language pipelines

Where YeetCode goes beyond Clearsilver (and beyond StringTemplate) is the grammar stage. Instead of requiring you to write code that populates the data layer, you can write a PEG grammar that parses an input syntax directly into the schema-validated HJSON intermediate.

This turns YeetCode into a full language-to-language transformation pipeline. Want to parse `.proto` files and generate C# classes? Write a protobuf grammar, define a schema for the intermediate, and write a C# template. Want to add TypeScript output? Write one new template file — the grammar and schema don't change.

The intermediate data file is always there, always inspectable. If your generated output looks wrong, you can open the HJSON and see exactly what the parser produced. If the parse looks right but the output is wrong, you know the bug is in the template. Each stage is independently debuggable.

## What's here now

YeetCode is still in active development. The core pipeline works — grammars parse, schemas validate, templates generate. I'm building real things with it (the style system codegen in my game engine runs on YeetCode). The VSCode extension provides syntax highlighting for HJSON, YTSON, templates, and grammar files.

I'd love to hear from anyone who's done code generation with template engines, dealt with the pain of debugging opaque AST-to-text emitters, or just thinks data-driven templating is an underappreciated idea. Pull up a chair.

— David