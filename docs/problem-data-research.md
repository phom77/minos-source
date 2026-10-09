# Kho dữ liệu đề bài cho Minos

Research ngày 2026-10-08; cập nhật follow-up cùng ngày về giới hạn tests và so sánh định dạng. Phần đánh giá nguồn là đề xuất, chưa phê duyệt mở rộng milestone. Follow-up chỉ triển khai giới hạn 1.500 tests theo yêu cầu chủ dự án; không tạo/publish problem trong ứng dụng, import nguồn, tải toàn bộ dataset hoặc chạy generator bên ngoài.

## Kết quả chính

Ưu tiên nguồn do tác giả/ban tổ chức công bố cùng dữ liệu chấm và giấy phép rõ ràng: Library Checker và các contest package được phép tái sử dụng. CodeContests là ứng viên mở rộng quy mô; APPS hữu ích cho khảo sát nhưng chưa nên trở thành kho chấm production bằng cách import nguyên bộ. Số đề trong nguồn không phải số đề đã đủ điều kiện dùng trên Minos.

## Các nền tảng tạo đề và test như thế nào

LeetCode có vai trò Problem Adder, Internal Contest Tester và Content Coordinator: tạo đề, chuẩn bị bộ test và kiểm tra chất lượng là công việc của đội nội dung. [Nguồn tuyển dụng chính thức](https://leetcode.com/jobs/).

Codeforces cung cấp Polygon cho toàn bộ quy trình soạn đề, generator, lời giải đúng/sai và validation. [Polygon](https://polygon.codeforces.com/). Ban tổ chức ICPC cũng nhận đề từ cộng đồng tác giả qua Call for Problems; ví dụ BAPC yêu cầu statement, samples, lời giải đúng và phân tích. [BAPC CfP](https://jury.bapc.eu/).

Generator tạo nhiều input từ seed và các nhóm cấu trúc; lời giải chuẩn sinh đáp án, validator kiểm tra input. Testlib cung cấp công cụ generator/validator/checker. [Testlib](https://github.com/MikeMirzayanov/testlib). Đối chiếu lời giải độc lập hoặc brute force trên miền nhỏ và thử các lời giải sai có chủ đích là đề xuất kiểm định cho Minos, không phải khẳng định về toàn bộ quy trình nội bộ của từng nền tảng.

Phân biệt số tình huống logic, số file input và số lượt sandbox. Một input có thể chứa nhiều trường hợp nếu đặc tả vốn quy định như vậy; không được tự nối các input độc lập. Con số gần 1.000 trên UI không chứng minh nền tảng tạo 1.000 container. Nhiều test ngẫu nhiên cùng phân phối vẫn có thể bỏ sót một lỗi biên.

## Nguồn dữ liệu cụ thể

| Nguồn | Dữ liệu và quy mô kiểm chứng | Quyền sử dụng / điểm cần kiểm tra | Đề xuất |
| --- | --- | --- | --- |
| [Library Checker](https://github.com/yosupo06/library-checker-problems) | 235 ID duy nhất trong categories.toml ở SHA `1814c4e5205517e368bb57a8d1127eb961cfeaae`; statement, generator, validator, checker và lời giải C++ | Root [LICENSE](https://github.com/yosupo06/library-checker-problems/blob/master/LICENSE) là Apache 2.0; giữ notice và kiểm tra giấy phép riêng của thành phần | Nguồn đầu tiên để lập catalog; thiên về cấu trúc dữ liệu/thuật toán, nhiều bài nâng cao |
| [KTH Challenge 2022](https://challenge.csc.kth.se/2022/) | Trang tổ chức dẫn [repo GitLab](https://gitlab.com/bjorn-martinsson/KTH_CHALLENGE_2022) chứa tests, judge solutions và generators | Trang tổ chức công bố tất cả tài liệu theo CC BY-SA 2.5 SE | Nguồn contest có bằng chứng giấy phép rõ; tiếp tục kiểm kê từng bài và checker |
| [NWERC 2021](https://2021.nwerc.eu/) / [BAPC 2018](https://2018.bapc.eu/problems.html) | NWERC công bố package chính 97 MB, practice 41 MB; BAPC dẫn ZIP statement/tests/jury solutions theo Domjudge | Có download không đồng nghĩa đã xác nhận quyền tái xuất bản; kiểm tra metadata/license từng package | Kho contest chính thức để mở rộng sau kiểm định; không coi mọi năm có cùng giấy phép |
| [CodeContests](https://github.com/google-deepmind/code_contests) | Dataset có statement, input/output và lời giải đúng/sai nhiều ngôn ngữ; bản gốc dùng Riegeli/protobuf, khoảng 3 GiB | README công bố non-code CC BY 4.0, code Apache 2.0, đồng thời ghi rõ thành phần bên thứ ba có thể theo điều khoản riêng | Ứng viên bulk, cần kiểm định nguồn, tests, checker, C++20 và giới hạn từng bài |
| [APPS](https://huggingface.co/datasets/codeparrot/apps) | 10.000 đề, 131.777 tests; có lời giải Python, một số bài nhận lời gọi hàm; card ghi thiếu tests ở 195 mẫu train và thiếu lời giải ở 1.235 mẫu test | Mirror ghi MIT, nhưng [paper gốc](https://arxiv.org/pdf/2105.09938) Appendix A ghi data CC BY-SA 3.0, code MIT và viện dẫn fair use nghiên cứu cho một số nguồn | Chưa ưu tiên để tái xuất bản thành ngân hàng đề; cần giải quyết provenance/license và chất lượng |

Library Checker có [hướng dẫn cấu trúc](https://github.com/yosupo06/library-checker-problems/blob/master/docs/guideline.en.md); input/output được sinh từ generator và lời giải chuẩn, không nhất thiết được commit sẵn. Ví dụ [Unionfind info.toml](https://raw.githubusercontent.com/yosupo06/library-checker-problems/master/data_structure/unionfind/info.toml) khai báo 18 file test với tối đa 200.000 truy vấn trong một input. Đây là ví dụ vì sao không cần 1.000 file để có workload lớn.

CodeContests [dataset card](https://huggingface.co/datasets/deepmind/code_contests) khai báo 13.328 train + 117 validation + 165 test = 13.610 bản ghi. Khi đọc [size API](https://datasets-server.huggingface.co/size?dataset=deepmind/code_contests), Parquet viewer trả 4.044 dòng và `partial: true`; đó là phần đã chuyển đổi/hiển thị, không phải bằng chứng bộ gốc chỉ còn 4.044 đề. Chưa tải bộ gốc để kiểm đếm hoặc xác nhận số bài có đầy đủ private tests. Không phải bản ghi nào cũng có tests/limits/lời giải C++ phù hợp.

APPS card cũng cảnh báo false positives do thiếu độ bao phủ test. Nhãn license của mirror không tự giải quyết quyền đối với nội dung từ website nguồn. Đây là lý do cần kiểm định trước khi đưa dataset nghiên cứu lên một website cho người dùng luyện tập.

## Vì sao không lấy thẳng đề từ LeetCode/Codeforces

[LeetCode Terms](https://leetcode.com/terms/) bảo lưu quyền đối với nội dung, bao gồm questions. [Codeforces Terms](https://codeforces.com/terms) hạn chế thương mại hóa website materials. Đây không phải cấp phép chung để sao chép ngân hàng đề sang Minos.

API `problemset.problems` của Codeforces trả metadata/statistics; [Problem object](https://codeforces.com/apiHelp/objects#Problem) có ID, name, rating và tags, không có statement/full judge tests. Polygon là công cụ tác giả chuẩn bị/chia sẻ problem package, không phải API tải mọi bộ test của Codeforces. Repo lời giải hoặc sample scraped không thay thế được judge dataset.

Đối với KTH CC BY-SA, [license deed](https://creativecommons.org/licenses/by-sa/2.5/se/deed.en) cho phép share/adapt có điều kiện ghi công, liên kết giấy phép và ShareAlike cho bản thích nghi. Cần phân biệt giấy phép nội dung đề với giấy phép phần mềm của Minos.

## Điều kiện tương thích Minos hiện tại

Bằng chứng code: `src/Minos.Domain/Problems/Problem.cs` chỉ có `BuiltInChecker.Exact`, tối đa 1.500 private test files (chủ dự án phê duyệt nâng từ 100 ngày 2026-10-08), 20 samples, time limit 100–10.000 ms và memory 16–1.024 MiB. API authoring nhận ZIP cho từng test; chưa có importer cho ICPC/Polygon/Library Checker. Worker dùng C++20, stdin/stdout và sandbox mới cho mỗi test; chạy hết mọi test là quy tắc hiện tại. Migration `20261008160526_ExpandProblemTestCapacity` cập nhật constraint PostgreSQL; không sửa migration lịch sử hoặc cắt dữ liệu.

- Không nhận interactive, output-only, scoring, file-I/O đặc biệt hoặc function-call khi chưa có hỗ trợ tương ứng.
- Không thay checker gốc bằng Exact một cách âm thầm. Ngay [checker mẫu A+B của Library Checker](https://github.com/yosupo06/library-checker-problems/blob/master/sample/aplusb/checker.cpp) so chuỗi token, nên khác từng-byte khi có whitespace/newline. Output xác định duy nhất về toán học chưa bảo đảm byte-exact compatibility.
- Bài nhiều đáp án hợp lệ hoặc sai số số thực cần checker riêng; một expected output không đủ. Chọn bài khác hoặc trình chủ dự án một đề xuất checker riêng có kiểm chứng trước khi triển khai.
- Giữ bộ test đầy đủ trong giới hạn 1.500, không tự cắt lấy phần đầu khi vượt giới hạn. Kiểm định độ bao phủ và giữ phiên bản nguồn; tăng số file không tự cải thiện chất lượng test. Số file lớn làm tăng thời gian chấm và dung lượng lưu trữ; không diễn giải giới hạn mới thành cam kết độ trễ.
- Statement Markdown/LaTeX, hình ảnh, samples, units và giới hạn phải được chuyển đúng. Lời giải chuẩn cần kiểm tra bằng toolchain/sandbox Minos; không giả định kết quả Accepted ở nền tảng khác sẽ giống nhau.
- Test được công bố trong repo vẫn dùng được để luyện tập nhưng người dùng có thể tra được; không xem đó là bộ test bí mật chống gian lận.

## Hướng xây kho nguồn được đề xuất

Lập catalog ngoài dữ liệu production, mỗi mục ghi source URL/ID, SHA/checksum, author/license/attribution, statement/assets, samples, số private tests, generators/seeds, checker gốc, constraints, reference solutions và trạng thái kiểm định. Phân biệt `candidate`, `blocked-license`, `blocked-capability` và `verified`; các trạng thái này là đề xuất cho kho tuyển chọn, không phải state machine mới trong Domain.

Thứ tự: kiểm kê Library Checker và contest có giấy phép rõ; chọn một tập cân bằng bài cơ bản/trung bình; kiểm tra dữ liệu và checker; chỉ sau đó thiết kế import qua authoring/publish hiện có. CodeContests bổ sung quy mô sau khi nguồn nhỏ đã xác nhận được cách nhập và chấm. APPS giữ ở nhóm nghiên cứu cho đến khi giải quyết các điểm nêu trên.

Mục tiêu khởi đầu đề xuất là 50–100 đề đã kiểm định, không phải cam kết có sẵn 50–100 đề tương thích. Chưa xác định số `verified`: lần research ban đầu chỉ đọc tài liệu, metadata và một số file mẫu. Follow-up đã phê duyệt và triển khai mở rộng giới hạn 1.500 tests cùng constraint; chưa mở rộng checker, kiểu I/O hoặc import đề.

## Đối chiếu định dạng LeetCode và Codeforces

So sánh này nói về hợp đồng bài/chấm và cấu trúc dữ liệu, không phải giao diện. UI kiểu LeetCode có thể dùng cho bài stdin/stdout mà không đổi hợp đồng chấm.

| Nguồn | Mức độ gần LeetCode / Codeforces | Định dạng vật lý | Phù hợp với Minos |
| --- | --- | --- | --- |
| CodeContests | Gần Codeforces nhất trong nhóm dataset lớn; enum nguồn có CODEFORCES và metadata contest ID/index/rating/tags | Riegeli/protobuf; description, public/private/generated tests là cặp chuỗi input/output, solutions có ngôn ngữ; giới hạn time/memory và file I/O là optional | Ưu tiên khảo sát để mở rộng ngân hàng đề kiểu Codeforces; lọc bài stdin/stdout, checker, giới hạn và quyền từng nguồn trước import |
| APPS | Gần kiểu LeetCode ở phần call-based, không phải toàn bộ dataset; cũng có standard input | question, starter_code, input_output JSON với inputs/outputs và fn_name nếu có; lời giải Python | Chưa phù hợp để nhập phần gọi hàm: Minos chưa có harness C++ với signature, parser/serializer và so sánh giá trị trả về; còn điểm license/coverage nêu trên |
| KTH / NWERC / BAPC contest packages | Gần Codeforces/ICPC: đọc stdin, ghi stdout, samples, limits và bộ test | Problem packages gồm statement, test files, jury solutions và validators/checkers tùy package | Hợp với pipeline hiện tại sau chuyển format; ưu tiên package có giấy phép rõ như KTH 2022, không suy ra quyền từ link tải |
| Library Checker | Gần Codeforces về stdin/stdout, nhưng nội dung thiên về kiểm tra thuật toán/cấu trúc dữ liệu | task.md có template, info.toml, generator, verifier, checker và sol/correct.cpp | Nguồn kỹ thuật rõ ràng để thử pipeline; không nên coi là ngân hàng đề đại chúng cân bằng chủ đề/độ khó |

Bằng chứng: [schema CodeContests](https://github.com/google-deepmind/code_contests/blob/main/contest_problem.proto) có public/private/generated tests, C++ solutions và metadata Codeforces; đa số dùng stdin/stdout, một số dùng file. [APPS card](https://huggingface.co/datasets/codeparrot/apps) mô tả starter_code/fn_name và ví dụ Codeforces. [Evaluator APPS gốc](https://github.com/hendrycks/apps/blob/main/eval/testing_util.py) phân biệt standard_input và call_based, khởi tạo class Solution khi có: đây là cơ sở cho nhận định phần gọi hàm gần LeetCode, không chứng minh mọi bài APPS đều từ LeetCode. [Library Checker guideline](https://github.com/yosupo06/library-checker-problems/blob/master/docs/guideline.en.md) ghi cấu trúc generator/statement/checker và lời giải stdin/stdout. [KTH 2022](https://challenge.csc.kth.se/2022/) và [NWERC 2021](https://2021.nwerc.eu/) công bố package chính thức.

Đề xuất theo mục tiêu hiện tại: dùng bài kiểu Codeforces với UI giải bài đã thiết kế như LeetCode. Chọn contest có giấy phép rõ cho batch đầu; CodeContests là ứng viên quy mô lớn sau kiểm định quyền và chất lượng. Nếu yêu cầu người dùng chỉ viết hàm/class như LeetCode, cần một slice riêng cho hợp đồng hàm và trusted harness; tăng số tests không cung cấp khả năng đó. Không có nguồn nào trong nhóm này đã được xác nhận là ngân hàng LeetCode hoàn chỉnh, đủ private tests và có quyền tái xuất bản mọi bài.

## Kiểm chứng thay đổi giới hạn

Build toàn solution trên Windows và Linux Release qua, không warning/error. Domain: 22/22 qua. Linux PostgreSQL/HTTP integration: 188 qua, 7 skipped (Windows ACL, native profile và opt-in benchmarks), không lỗi. Dùng database/artifact fixtures tạm qua `MINOS_TEST_RUNNER=docker MINOS_TEST_ALL=1 MINOS_TEST_BENCHMARK=0 bash scripts/benchmark-native-judging.sh minos-checks:test-capacity-1500-final`; log local ignored ở `artifacts/test-capacity-1500.log`.

Regression kiểm tra 1.500/1.501 ở Domain và constraint PostgreSQL, giữ snapshot 100 tests khi nâng schema, lưu version 1.500 tests, down migration bị chặn và rollback transaction khi còn version vượt 100. Pipeline chấm với controlled execution evidence lưu/trả đủ 1.500 kết quả WrongAnswer hỗn hợp và 1.500 NotExecuted khi CompilationError qua HTTP; không phải benchmark 1.500 sandbox thật hoặc cam kết độ trễ. Các fixture Docker thật hiện có cũng qua. Migration chưa áp dụng vào database ứng dụng; xem [runbook nâng cấp/rollback](operations/development-runbook.md#retention-and-rollback).
