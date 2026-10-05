# Province and district data

`provinces.json` and `districts.json` list Turkey's 81 provinces (code = licence plate code) and 973 districts.

- **Source:** the PTT postal code list (https://postakodu.ptt.gov.tr), as packaged in the npm package
  [`turkey-neighbourhoods`](https://github.com/muratgozel/turkey-neighbourhoods) version 4.0.3 (files
  `src/data/cityNamesByCode.json` and `src/data/districtsByCityCode.json`).
- **Licence:** MIT (below).
- **District ids** are `provinceCode * 1000 + n`, assigned once in Turkish alphabetical order. They never change:
  when the source adds a district, append it with the next free id; keep removed districts and stop offering them
  instead of reusing ids.

```
MIT License

Copyright (c) 2020 Murat Gözel

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
