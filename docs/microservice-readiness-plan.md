# Kế hoạch chuẩn bị Minos cho microservice — 3 phase

Ngày: 2026-10-12
Trạng thái: Phase 1 đã triển khai và kiểm chứng xong ngày 2026-10-13; [candidate và bằng chứng](phase1-readiness-results.md) sẵn sàng để owner review. Nghiệm thu development MVP vẫn chờ owner; Phase 2–3 vẫn là kế hoạch.

Owner yêu cầu tạo kế hoạch, sau đó yêu cầu triển khai toàn bộ Phase 1. Phạm vi này không xác nhận nghiệm thu M7 và không cấp phép M8, broker, distributed Worker, microservice extraction hoặc public hosting. Các thay đổi vượt ranh giới hiện tại chỉ được thực hiện sau khi có bằng chứng và quyết định phạm vi tương ứng.

## Mục tiêu và nguyên tắc

Hoàn thiện một phiên bản Minos kiểm chứng được, đo được năng lực và xác định đúng ranh giới trước khi quyết định tách service. Kết quả hợp lệ có thể là tiếp tục modular monolith nếu việc tách chưa mang lại lợi ích đủ rõ.

- Giữ năm production projects và các test projects hiện có trong Phase 1–2. Không chia Domain/Application/Infrastructure thành service.
- Không lặp lại M1–M6 hoặc viết lại logic đúng. Chạy regression phù hợp với thay đổi và giữ bằng chứng lịch sử theo revision/profile.
- Giữ submission/job/contest association nguyên tử; published versions bất biến; quyền truy cập được kiểm tra trong Application.
- Giữ complete-test judging, verdict precedence, attempt tokens, bounded retries, sandbox isolation và cleanup authority.
- Docker vẫn là profile mặc định. Native Isolate/multiple boxes chỉ được tuyên bố đạt khi đã kiểm chứng đúng host/configuration đó.
- Không thêm công nghệ hoặc abstraction chỉ để chuẩn bị cho một nhu cầu giả định. ADR chỉ ghi quyết định ảnh hưởng correctness, security hoặc tiến hóa lâu dài.

## Bằng chứng hiện tại và điểm cần xác nhận

| Bằng chứng | Ý nghĩa đối với kế hoạch |
| --- | --- |
| [M7 reliability](milestones/m7-reliability.md) | Backend hoàn thành; checklist vẫn ghi owner acceptance và hosted CI chưa xác nhận. Không suy ra trạng thái thực tế của GitHub từ tài liệu cũ. |
| [Kiểm chứng 2026-10-09](changes-verification-20261009.md) | Có build, frontend/backend suites và browser journey qua Worker thật; các giới hạn Isolate, Google, recovery rehearsal và npm advisory được ghi riêng. |
| [CI hiện có](../.github/workflows/build.yml) | Chạy foundation backend và lưu TRX/workload; cần kiểm tra coverage frontend/browser trước khi thêm job còn thiếu. |
| [Runbook](operations/development-runbook.md) | Đã có backup/restore/recovery/alerts; POST mất phản hồi chưa có client idempotency key và có thể tạo submission thứ hai. |
| [Evaluation ports](../src/Minos.Application/Evaluation/EvaluationPorts.cs) | Delivery chỉ mang job ID; targeted claim, lease và completion đã có ranh giới Application. |
| [Judging metrics](../src/Minos.Infrastructure/Evaluation/JudgingMetrics.cs) | Có phase durations và compiler memory; cần thu thập/báo cáo tải thay vì xây lại instrumentation. |
| [Kế hoạch cải thiện judging](judging-improvement-plan.md) | Đã có benchmark/concurrency evidence; Phase 2 mở rộng workload còn thiếu và so sánh trên cùng điều kiện. |

## Phase 1 — Chốt baseline và sửa khoảng trống correctness/release

Mục tiêu: có một revision nghiệm thu rõ ràng, kiểm thử tự động phù hợp và hành vi nộp bài an toàn khi người dùng retry.

### Đầu việc theo thứ tự

| ID | Công việc | Layer/file liên quan | Đầu ra và kiểm chứng |
| --- | --- | --- | --- |
| P1.1 | Chọn candidate revision; đối chiếu bằng chứng hiện có với code/profile hiện tại và xác nhận hosted CI thật. | Roadmap, verification report, workflow | SHA, image/toolchain/configuration, CI run URL, passed/skipped checks và giới hạn được ghi rõ. Không ghi một run chưa thực hiện là passed. |
| P1.2 | Bổ sung phần CI frontend còn thiếu: locked install, production build, regression suite; browser smoke dùng fixture cách ly. Tái dùng harness hiện có và chọn subset có giá trị. | `.github/workflows/build.yml`, `web/minos-web`, browser harness | CI phát hiện lỗi build/validation/navigation; browser kiểm tra author/solver/contest/result và keyboard/responsive states có liên quan. Backend/sandbox suite hiện có tiếp tục chạy. |
| P1.3 | Thiết kế rồi bổ sung idempotency cho create submission ở practice và contest. Đây là sửa correctness cho mất phản hồi/retry, không phải thay delivery hoặc broker. | Application use cases/ports, Infrastructure persistence/migration, Api transport, frontend submit | Cùng account/key và payload trả cùng submission; payload khác với cùng key trả conflict an toàn. Unique constraint và giao dịch bảo vệ race; admission bị từ chối không để lại submission/job/key dở dang. |
| P1.4 | Review lại npm advisory đã ghi; nâng bộ Angular/toolchain đồng bộ nếu cần, kiểm tra advisories tại thời điểm làm. | `web/minos-web` manifests/lockfile | Build/tests/browser checks phù hợp passed; phân biệt runtime/build-time reachability và ghi các deferred findings có lý do/ngày review. Không dùng force upgrade thiếu kiểm chứng. |
| P1.5 | Chốt acceptance checklist và operating notes cho candidate. Chỉ bổ sung/rehearse recovery checks khi thay đổi làm bằng chứng cũ không còn đủ. | Existing scripts/runbook/M7 checklist | Owner review được một candidate cụ thể; backup/restore, SIGKILL, database outage evidence có revision/profile rõ ràng. Nghiệm thu là quyết định của owner. |

P1.3 phải chốt trước implementation: phạm vi key theo account, route/target và pinned version; thời hạn/replay policy; lưu fingerprint thay vì source trong metadata; cách frontend giữ key khi retry cùng nội dung và tạo key mới khi chủ động nộp bài mới. Request phải vẫn kiểm tra active-account, ownership/membership và không cho replay tạo công việc mới ngoài cửa sổ contest. Chốt rõ replay một submission đã được nhận trước giờ kết thúc; không chạy lại admission hoặc tạo attempt mới. Hết TTL không được frontend hiểu là retry vẫn an toàn; lưu bản ghi bounded metadata hoặc công bố giới hạn replay rõ ràng.

Kiểm thử có ý nghĩa cho P1.3 nằm trong các test projects hiện có: concurrent identical requests, conflict payload/target, account khác, disabled account, response loss after commit, rejected admission/rollback và contest end-time boundary. Frontend phân biệt retry cùng lần nộp với một lần nộp mới.

Live Google consent chỉ là acceptance item khi cấu hình provider thật; vẫn ghi deferred nếu chưa có client. Password recovery/email verification là công việc cho public release, chưa triển khai chỉ để đạt phase này.

### Exit criteria

- [x] Candidate revision có CI backend/frontend cần thiết và end-to-end evidence với stack thật.
- [x] Retry nộp cùng request không tạo submission/job trùng; admission, quyền truy cập và thời gian contest giữ nguyên.
- [x] Dependency findings được xử lý hoặc triage rõ; không tuyên bố audit-clean khi còn advisory.
- [x] Owner có đủ bằng chứng để quyết định nghiệm thu development MVP; giới hạn còn lại không bị che thành passed.

Deliverable: báo cáo candidate ngắn, CI links, regression evidence và cập nhật checklist/runbook cần thiết. Nếu owner chưa nghiệm thu, có thể tiếp tục đo/thiết kế độc lập nhưng không ghi MVP accepted hoặc bắt đầu M8.

## Phase 2 — Đo tải, hoàn thiện quan sát và thiết kế ranh giới

Mục tiêu: chứng minh nhu cầu tách và chuẩn bị hợp đồng dữ liệu/lỗi mà chưa thay topology.

### Đầu việc theo thứ tự

| ID | Công việc | Layer/file liên quan | Đầu ra và kiểm chứng |
| --- | --- | --- | --- |
| P2.1 | Định nghĩa workload và mục tiêu trước khi chạy; tận dụng benchmark/concurrency scripts hiện có. | `scripts/benchmark-judging.sh`, `scripts/check-judging-concurrency.sh`, performance report | Workload manifest có số owner/jobs/tests, loại bài, source fixture trusted, host resources, runtime/config/toolchain và cold/warm distinction. |
| P2.2 | Chạy workload tăng dần trong fixture cách ly, tách queue, admission, compile, runtime, artifact I/O, cleanup và result persistence. | Existing metrics, Worker/Infrastructure, scripts | Báo cáo p50/p95, throughput, peak CPU/RAM/disk, retry/SystemFailed và admission rejection; nêu số mẫu/cách tính, không coi workload nhỏ là capacity production. |
| P2.3 | Thu thập metrics/log hiện có; bổ sung correlation hoặc measurement chỉ ở khoảng trống được chứng minh. | Api/Worker hosting, Infrastructure diagnostics, runbook | Tìm được một submission từ admission đến accepted attempt hoặc failure; report/dashboards tối thiểu và alert recovery path được kiểm chứng. |
| P2.4 | Lập bảng quyền sở hữu và giao dịch cho accounts, problem/version/artifact, submission, contest, job/attempt/result. | Application ports/use cases, Infrastructure repositories/grants | Phân biệt trạng thái hiện tại và target đề xuất; chỉ rõ writer, authorized readers, source of truth, consistency, retention và migration implications. |
| P2.5 | Viết extraction contract cho một candidate; ưu tiên xem xét judging/execution nếu số liệu hỗ trợ. | Application execution/evaluation ports; transport design chưa triển khai | Contract version, trusted identity, claim/renew/complete authority, pinned artifact access, bounded payload/evidence và error matrix. In-process byte-array ports không tự trở thành wire DTO. |
| P2.6 | So sánh giữ nguyên, tuning single-host và tách service. | Decision brief | Cùng workload/correctness; nêu lợi ích đo được, vận hành/chi phí mới, failure modes và rollback. Nếu mục tiêu là học, ghi learning objective thay vì ngụy tạo capacity need. |

Workload tối thiểu gồm CPU-heavy, compile-heavy, nhiều test ngắn, output/memory/runtime failures và burst practice/contest chung admission. Kiểm tra API latency khi Worker bận, fairness/starvation, cancellation/shutdown và giới hạn slot/memory. Có thể dùng cả fixture 100 test và capacity case gần 1.500 test; không cần mọi run đều chạy ở tối đa. Đo native riêng chỉ khi có host đáp ứng chính sách; không bật cấu hình bỏ qua validation để lấy số đẹp.

Các mục tiêu cần chốt trước run: queue p95, completion p95, API p95 trong tải chấm, retry/SystemFailed threshold và headroom CPU/RAM/disk. Chỉ sử dụng 120-second queue target trong phạm vi fixture M7 nếu giữ đúng workload đó. Ngưỡng mới dựa trên hành trình người dùng/workload, không lấy mặc định 32 active submissions làm năng lực phục vụ.

Correlation IDs có thể xuất hiện trong trusted logs/traces theo quyền truy cập phù hợp; không làm labels metrics có cardinality cao. Không log source, hidden input/answers, stdout/stderr, credentials hoặc lease tokens.

Extraction contract phải trả lời: delivery trùng; process chết trước/sau claim; lease hết hạn; completion commit nhưng mất acknowledgement; dependency unavailable; artifact thiếu/corrupt; cleanup không chắc chắn; shutdown giữa stage; phiên bản cũ/mới cùng tồn tại. Mỗi trường hợp có expected persisted state, retry owner, giới hạn retry và regression evidence dự kiến.

Định hướng để đánh giá, chưa phải kiến trúc chấp nhận: giữ admission/submission/job/contest association và kết quả chính thức trong một authority; execution host chịu trách nhiệm sandbox, scheduling, evidence và cleanup. Không chia compile/runtime/checker thành các service riêng. Nếu chọn hướng khác, phải giải thích lợi ích và cách giữ nguyên đảm bảo correctness.

### Exit criteria

- [ ] Có report tái lập được trên profile mục tiêu; biết nút thắt và phạm vi kết luận.
- [ ] Metrics/log/alerts đủ chẩn đoán queue stuck, lease loss, cleanup failure và overload.
- [ ] Ownership, transaction boundaries, artifact security và version/error contract được viết rõ.
- [ ] Decision brief khuyến nghị một lựa chọn với bằng chứng; broker/outbox/distributed hosting chỉ xuất hiện khi có yêu cầu cụ thể.

Deliverable: performance report, ownership/contract specification và go/no-go brief. Không cần thêm project/service để hoàn tất phase này.

## Phase 3 — Quyết định và triển khai lần tách đầu tiên có kiểm soát

Mục tiêu: giải quyết đúng vấn đề đã đo, hoặc kết thúc readiness plan bằng quyết định giữ kiến trúc hiện tại.

### Đầu việc theo thứ tự

| ID | Công việc | Đầu ra và kiểm chứng |
| --- | --- | --- |
| P3.1 | Owner review decision brief. Chọn giữ nguyên/tuning, hoặc đúng một extraction với phạm vi và success criteria cụ thể. | Quyết định có lý do; nếu không tách thì ghi trigger để đánh giá lại. Không bắt buộc chuyển microservice để coi kế hoạch hoàn thành. |
| P3.2 | Nếu tách được chấp nhận: ghi ADR cho architecture/hosting/data changes và cập nhật AGENTS.md đúng phạm vi trước implementation. | Số projects nếu cần thay đổi được chứng minh; contract, migration, grants/service credentials, retention, release order và rollback cụ thể. |
| P3.3 | Implement một vertical slice trên fixture development. Tái dùng Worker và ports hiện có; thêm transport/adapters cần thiết theo thiết kế đã chấp nhận. | Một submission đi xuyên ranh giới mới và trả owner result đúng; authorization, pinning, complete evidence và private artifacts được giữ. Không đưa domain entities/DbContext qua transport. |
| P3.4 | Kiểm chứng giao tiếp và sự cố tại ranh giới mới. | Contract tests trong test projects hiện có và real integration: duplicates, timeout/disconnect, stale completion, restart, credential denial, artifact failure, cleanup uncertainty, mixed versions. |
| P3.5 | Rehearse deployment/rollback riêng và so sánh trước/sau trên cùng workload. | Thành phần tách nâng cấp/rollback theo compatibility window; logs truy vết được; success criteria đạt và API/correctness không regress. Lưu CI/build/image evidence cho release candidate. |
| P3.6 | Chốt kết quả và runbook. | Owner review kết quả, costs/limits, rollout/rollback/recovery steps; không suy ra public production hay distributed-host acceptance từ fixture development. |

Chỉ bắt đầu P3.2–P3.6 sau quyết định P3.1 chấp nhận extraction. Các việc sau là điều kiện của lần tách được chọn, không phải hạng mục phải cài sẵn:

- Nếu dùng broker: atomic durable handoff, publisher confirmation, acknowledgement, bounded retry/duplicate handling và reconciliation khi delivery thất lạc. Outbox chỉ thêm khi giải quyết yêu cầu đó.
- Nếu chạy nhiều host: artifact access/verification, per-host recovery ownership, host identity/resources và lease behavior phải được thiết kế/kiểm chứng riêng. Không tái sử dụng global recovery labels/lock theo cách một host dọn sandbox của host khác.
- Nếu đưa claim/renew/complete qua transport: trusted authentication, least privilege, guarded attempt authority và bounded request/response; contestant/browser không được gọi các operations này.
- Nếu service sở hữu durable data mới: writer và migration owner riêng; không để hai service tự do sửa cùng bảng. Một PostgreSQL instance vật lý có thể được đánh giá, nhưng data ownership/grants phải độc lập.

### Exit criteria

- [ ] Nếu giữ nguyên: có lý do dựa trên số liệu, tuning cần thiết được kiểm chứng và trigger đánh giá lại rõ ràng.
- [ ] Nếu tách: lợi ích hoặc learning objective được chứng minh; correctness/security giữ nguyên; lỗi mạng/phiên bản/phục hồi có evidence.
- [ ] Nếu tách: có deployment/rollback riêng thực tế; shared code/schema không buộc mọi component phải nâng cấp đồng thời.
- [ ] Runbook và engineering rules phản ánh quyết định được chấp nhận; không có unused broker, abstraction hoặc project.

## Feature và public-release work theo dõi riêng

Các mục này không phải prerequisite kỹ thuật cho microservice. Khi owner chọn làm, lập slice/acceptance riêng và đặt ưu tiên theo nhu cầu người dùng.

| Hạng mục | Điều kiện/phạm vi |
| --- | --- |
| Contest scoring/standings | Chốt scoring, penalty, tie-break, freeze/finalization; hiện contest chỉ dùng published public problems. |
| Custom Run | M8 cần phê duyệt riêng; bounded input/output, retention và admission/scheduling bảo vệ official judging. |
| Tags/search/bookmarks, progress, editorial | Feature product độc lập; chỉ hiển thị dữ liệu đã có contract thật. |
| Live Google, password recovery/email verification | Provider acceptance và public-account delivery/security policy; không tạo fake controls. |
| Public hosting | Capacity, retention, secrets/key management, sandbox host threat model và operating policy cần quyết định riêng. |
| Ratings, additional languages, notifications | Không mở rộng scope của readiness plan; yêu cầu product policy và acceptance riêng. |

## Theo dõi thực hiện

Làm một slice tại một thời điểm theo thứ tự P1 → P2 → P3; P2 design/measurement độc lập có thể tiến hành khi P1 acceptance còn chờ owner. Với mỗi ID, ghi status, commit SHA, evidence, skips/limits và quyết định còn thiếu. Không gán ngày hoàn thành cố định trước khi có baseline và extraction scope; không tạo thêm hệ thống quản lý công việc cho kế hoạch này.

Kế hoạch hoàn thành khi baseline và readiness evidence đầy đủ, và Phase 3 có quyết định đã được thực hiện/kiểm chứng theo nhánh được chọn. Số lượng service không phải tiêu chí thành công.
