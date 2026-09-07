# Notebooks

Polyglot / .NET Interactive notebooks — the fastest way to try OfficeNet without creating a project.

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

| Notebook | Isi / Contents |
|---|---|
| [`00-Quickstart.ipynb`](00-Quickstart.ipynb) | Keempat library dalam beberapa sel / all four libraries in a few cells |
| [`WordNet.ipynb`](WordNet.ipynb) | Paragraf, style, tabel, header/footer, mail merge, PDF |
| [`ExcelNet.ipynb`](ExcelNet.ipynb) | Sel bertipe, formula, style, CSV/JSON, PDF |
| [`PowerPointNet.ipynb`](PowerPointNet.ipynb) | Slide, chart native, bentuk, HTML → slide, PDF |
| [`PdfNet.ipynb`](PdfNet.ipynb) | Menggambar, ekstraksi teks, gabung/pisah, anotasi, enkripsi |

Markdown cells are bilingual (English + Bahasa Indonesia); code is shared.

## Menjalankan / Running

1. Install the **Polyglot Notebooks** extension in VS Code.
2. Build once, because the notebooks reference the local assemblies rather than NuGet:

   ```bash
   dotnet build OfficeNet.sln -c Release
   ```

3. Open a notebook and run the cells.

The first cell references `../src/*/bin/Release/net10.0/*.dll` deliberately: the version on NuGet is
always one release behind whatever you are editing, which is exactly the wrong thing to be testing
against. Once you are consuming the published packages, swap that cell for:

```csharp
#r "nuget: Gravicode.OfficeNet, *"
#r "nuget: Gravicode.OfficeNet.Rendering, *"
```

Each notebook renders its output inline through `OfficeNet.Rendering`, so you see the document you
just built rather than a description of it.

## Bagaimana notebook ini dijaga tetap benar / How these stay correct

The notebooks are **generated**, and their code is **compiled by the test suite**:

```bash
python tools/gen_notebooks.py        # writes the .ipynb files
python tools/gen_notebook_tests.py   # lifts every code cell into tests/OfficeNet.Docs.Tests/Notebooks/
dotnet test tests/OfficeNet.Docs.Tests
```

A notebook is JSON, so nothing normally stops its code from rotting when an API is renamed. Lifting
every cell into a real test means a rename breaks the build instead of breaking a reader's
afternoon — the same check found sixteen wrong API names in the first draft of `docs/`.

Edit `tools/gen_notebooks.py`, not the `.ipynb` files: regenerating overwrites them.
