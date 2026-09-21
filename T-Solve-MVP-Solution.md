# T-Solve MVP — Giải pháp import, xây dựng knowledge và kiểm soát review

## 1. Mục tiêu demo

- Kết nối Jira qua REST API.
- Import lần đầu khoảng 500 ticket đã `Resolved/Closed`.
- Đồng bộ tăng dần khoảng 10 ticket mới mỗi ngày.
- Không yêu cầu review từng ticket.
- Chỉ solution đã được con người duyệt mới trở thành knowledge chính thức.
- Giảm tải cho manager bằng cách review theo solution/cluster và theo mức độ rủi ro.

Nguyên tắc quan trọng:

> Ticket là evidence đầu vào. Solution mới là đối tượng cần review và publish.

## 2. Pipeline dùng chung

Cả bulk import và daily import sử dụng chung các thành phần:

1. Jira Connector.
2. Raw Ticket Storage.
3. Data Cleaning và Privacy Masking.
4. Quality Gate.
5. Duplicate Detection.
6. Published Solution Matching.
7. Similarity Clustering.
8. Candidate Pool và Promotion Gate.
9. Solution Draft.
10. Risk Classification.
11. Review, Publish và Audit.

## 3. Luồng 1 — Initial Bulk Import

Luồng này được dùng một lần để xử lý khoảng 500 ticket lịch sử.

```text
Import toàn bộ ticket lịch sử từ Jira
        ↓
Lưu raw snapshot và source reference
        ↓
Kiểm tra, làm sạch và masking dữ liệu
        ↓
Quality Gate
        ├── Dữ liệu không an toàn → BLOCKED
        ├── Resolution không đủ giá trị → SEARCH_ONLY
        └── Đủ điều kiện → xử lý tiếp
        ↓
Phát hiện ticket trùng lặp
        ↓
Gom ticket tương tự thành cluster
        ↓
Đánh giá và xếp hạng từng cluster
        ↓
Chọn cluster có giá trị cao
        ↓
Tạo một Solution Draft cho mỗi cluster
        ↓
Phân loại rủi ro
   ┌──────────┴──────────┐
Thông thường          Rủi ro cao
   ↓                      ↓
Domain Reviewer       Manager/SME
   └──────────┬──────────┘
              ↓
       Approve và Publish
       theo từng đợt
```

### Chính sách vận hành

- Không đưa 500 ticket vào review queue.
- Một cluster có thể chứa nhiều ticket nhưng chỉ tạo một Solution Draft.
- Chỉ các cluster đứng đầu bảng xếp hạng được tạo draft trước.
- Cluster chưa đủ giá trị được giữ trong `Candidate Pool`.
- Dùng `BACKFILL_QUEUE` và publish theo từng wave.
- Backfill có thể tạm dừng khi daily queue tăng.

## 4. Luồng 2 — Daily Incremental Import

Luồng này xử lý khoảng 10 ticket mới mỗi ngày.

```text
Import ticket mới đã Resolved/Closed
                ↓
Lưu raw snapshot
                ↓
Làm sạch, masking và kiểm tra chất lượng
                ↓
Ticket có khớp Published Solution?
        ┌───────┴────────┐
       Có               Không
        ↓                 ↓
Kiểm tra scope       Ticket có giá trị
và evidence          tái sử dụng không?
        │             ┌───┴────┐
        │           Không      Có
        ↓             ↓         ↓
Auto-link         SEARCH_ONLY  Đưa vào cluster
Không review                       ↓
                           Đủ evidence chưa?
                             ┌─────┴─────┐
                            Chưa         Đủ
                             ↓            ↓
                        Candidate Pool  Tạo Solution Draft
                                          ↓
                                  Phân loại rủi ro
                                  ┌───────┴───────┐
                              Thông thường      Cao
                                  ↓               ↓
                           Domain Reviewer   Manager/SME
                                  └───────┬───────┘
                                          ↓
                                  Approve và Publish
```

### Ngoại lệ khi match solution

Nếu ticket khớp solution đã publish nhưng khác scope hoặc có evidence mâu thuẫn, hệ thống không auto-link như bình thường. Hệ thống tạo `Review Trigger` để kiểm tra solution hiện tại.

## 5. Làm sạch và kiểm tra dữ liệu

Bước này nằm ngay sau Raw Ticket Storage và trước deduplication, matching hoặc clustering.

### Làm sạch văn bản

- Loại HTML và Jira markup không cần thiết.
- Chuẩn hóa Unicode, khoảng trắng và xuống dòng.
- Loại chữ ký email và nội dung trả lời tự động.
- Rút gọn log hoặc stack trace trùng lặp.
- Chuẩn hóa category, issue type và các giá trị đồng nghĩa.
- Tách problem, resolution, root cause và resolution steps nếu có thể.

### Mask dữ liệu nhạy cảm

- Email.
- Số điện thoại.
- IP address.
- Employee ID.
- Access token, API key và secret.
- Thông tin định danh thuộc workspace nhạy cảm.

Raw payload được giới hạn quyền truy cập. Matching, clustering và AI chỉ sử dụng dữ liệu đã mask.

### Quality Gate

Mỗi ticket nhận một kết quả:

| Kết quả | Ý nghĩa |
|---|---|
| `BLOCKED` | Còn dữ liệu nhạy cảm hoặc dữ liệu lỗi, chưa được xử lý tiếp |
| `SEARCH_ONLY` | Được lưu để tìm lịch sử nhưng không tạo knowledge |
| `CANDIDATE_ELIGIBLE` | Đủ điều kiện đi vào matching và clustering |
| `MANUAL_TRIAGE` | Không đủ chắc chắn, cần người phụ trách phân loại |

Các resolution như `Done`, `Fixed`, `Completed`, ticket test, cancelled hoặc không có kết luận thường được đưa vào `SEARCH_ONLY`.

## 6. Điều kiện tạo Solution Draft

Không tạo draft cho mọi ticket hoặc mọi cluster. Chỉ promote khi:

- Có nhiều ticket tương tự; hoặc có một case đơn lẻ nhưng giá trị/rủi ro cao.
- Resolution đủ rõ và tương đối nhất quán.
- Có source ticket hoặc evidence hợp lệ.
- Xác định được applicability/scope.
- Không còn mâu thuẫn quan trọng chưa giải quyết.
- Cluster đạt ngưỡng knowledge value.

Knowledge value và risk phải được đánh giá riêng:

- `Knowledge value`: frequency, reusability, resolution quality, evidence quality và recency.
- `Risk`: security, production, finance, HR policy, legal và dữ liệu nhạy cảm.

## 7. Phân cấp review

- `Author/Curator`: kiểm tra và chuẩn hóa Solution Draft.
- `Domain Reviewer`: duyệt solution thông thường.
- `Manager/SME`: chỉ duyệt solution rủi ro cao, policy-sensitive hoặc có evidence mâu thuẫn.

Không bắt một solution thông thường đi qua cả Domain Reviewer và Manager nếu chính sách không yêu cầu.

Chỉ solution ở trạng thái `PUBLISHED` mới xuất hiện trong kết quả reuse mặc định.

## 8. Hai queue độc lập

| Queue | Nội dung | Chính sách |
|---|---|---|
| `BACKFILL_QUEUE` | Ticket/cluster từ 500 ticket lịch sử | Xử lý theo quota, có thể tạm dừng |
| `DAILY_QUEUE` | Khoảng 10 ticket mới mỗi ngày | Ưu tiên cao hơn và có SLA riêng |

Daily queue luôn được ưu tiên để bulk import không trở thành bottleneck vận hành.

## 9. Kịch bản demo MVP

### Demo bulk import

1. Kết nối Jira và import 500 ticket.
2. Hiển thị số ticket bị block, search-only, duplicate và đủ điều kiện.
3. Hiển thị số cluster được tạo.
4. Chứng minh một cluster gồm nhiều ticket chỉ tạo một Solution Draft.
5. Hiển thị Domain Review Queue và Manager Review Queue riêng.
6. Approve một solution và tìm lại solution đó cùng Jira evidence.

### Demo daily sync

1. Đồng bộ thêm 10 ticket mới.
2. Một số ticket auto-link với solution đã publish.
3. Một số ticket được lưu search-only.
4. Ticket có giá trị được thêm vào cluster hiện tại hoặc cluster mới.
5. Chỉ cluster đủ evidence mới sinh Solution Draft.

Demo phải hỗ trợ hai chế độ:

- `Real Jira`: dùng Jira URL, account, API token và project key từ cấu hình an toàn.
- `Mock Jira`: dữ liệu mẫu để có thể trình bày khi mạng hoặc Jira không khả dụng.

## 10. Dashboard và tiêu chí chứng minh tính khả thi

Dashboard cần hiển thị:

- Tổng ticket nhận từ Jira.
- Số ticket normalized thành công.
- Số ticket blocked và search-only.
- Số duplicate.
- Số ticket auto-match với published solution.
- Số cluster.
- Số Solution Draft.
- Domain reviewer queue size.
- Manager queue size.
- Tuổi lớn nhất của item trong queue.
- Số ticket trung bình trên mỗi solution.
- Candidate-to-published rate.
- Reuse count và usefulness feedback.

Công thức thể hiện mức giảm review:

```text
Review reduction = 1 - (Solution Draft cần review / Tổng ticket import)
```

Ví dụ minh họa, không hard-code:

```text
500 ticket → 20 Solution Draft
Review reduction = 1 - 20/500 = 96%
```

Số liệu khi demo phải được tính từ kết quả pipeline thực tế.

## 11. Guardrail

Chỉ auto-link mà không review khi:

- Match confidence vượt threshold.
- Cùng workspace/domain.
- Applicability tương thích.
- Solution chưa deprecated và chưa quá hạn review.
- Không có evidence mâu thuẫn.
- Không thuộc nhóm rủi ro cao.

Trong giai đoạn đầu, audit ngẫu nhiên 5–10% quyết định auto-match và search-only. Khi precision ổn định có thể giảm tỷ lệ audit. Nếu precision giảm, tăng audit hoặc siết threshold.

## 12. Kết luận thiết kế

T-Solve có hai luồng đầu vào nhưng dùng chung một pipeline tri thức:

- Bulk import xây knowledge ban đầu theo cluster và xử lý theo từng đợt.
- Daily import ưu tiên match solution đã có và chỉ tạo draft khi xuất hiện knowledge mới hoặc đủ evidence.

Thiết kế giảm bottleneck bằng cách không review từng ticket, review theo solution/cluster và chỉ chuyển case rủi ro cao cho Manager/SME. Độ tin cậy được giữ bằng evidence, applicability, version, audit và quy tắc chỉ solution đã human review mới được publish.
