import fs from "node:fs/promises";
import { FileBlob, SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const sourcePath = "tickets_r1_r5_with_comments.xlsx";
const outputDir = "outputs/01a0c416-14cb-7de0-a6f3-7fd359d22329";
const outputPath = `${outputDir}/tickets_anonymized_with_500_samples.xlsx`;
const previewPath = `${outputDir}/tickets_preview.png`;

const sourceWorkbook = await SpreadsheetFile.importXlsx(await FileBlob.load(sourcePath));
const sourceSheet = sourceWorkbook.worksheets.getItem("Tickets");
const sourceRows = sourceSheet.getRange("A2:G301").values;

const appMap = new Map([
  ["HRMS/TMS System", "HR & Timesheet"],
  ["Dashboard", "Project Dashboard"],
  ["CRM System", "CRM"],
  ["POA System", "Approval Portal"],
  ["EC", "Performance Review"],
  ["C-Ticket", "Support Portal"],
  ["C-HUB", "Resource Planning"],
  ["Sale", "Sales Operations"],
  ["MMS", "Resource Planning"],
  ["AMS", "Business Application"],
]);

const baseApps = [
  "HR & Timesheet",
  "Project Dashboard",
  "CRM",
  "Approval Portal",
  "Performance Review",
  "Support Portal",
  "Resource Planning",
  "Finance & Billing",
  "Identity & Access",
  "Reporting & Analytics",
  "Collaboration",
  "Document Management",
  "Procurement",
  "Asset Management",
  "Customer Support",
  "Mobile Application",
  "API Gateway",
  "Data Integration",
  "Learning Portal",
  "Booking System",
];

const issues = [
  { s: "Không đăng nhập được bằng SSO", d: "Người dùng thử nghiệm không thể đăng nhập qua SSO dù tài khoản đang hoạt động. Hệ thống quay lại màn hình đăng nhập mà không hiển thị lỗi rõ ràng.", r: "Đã làm mới phiên SSO và hướng dẫn đăng nhập lại trên trình duyệt mới." },
  { s: "Yêu cầu cấp quyền xem báo cáo", d: "Vai trò hiện tại chưa có quyền mở báo cáo tổng hợp theo đơn vị. Cần bổ sung quyền chỉ đọc theo đúng phạm vi dữ liệu mẫu.", r: "Đã gán vai trò chỉ đọc và xác nhận báo cáo mở bình thường." },
  { s: "Dữ liệu chấm công chưa đồng bộ", d: "Bản ghi chấm công của một ngày kiểm thử chưa xuất hiện sau lần đồng bộ tự động. Các ngày khác vẫn hiển thị bình thường.", r: "Đã chạy lại tác vụ đồng bộ và kiểm tra bản ghi xuất hiện đầy đủ." },
  { s: "Ngày kết thúc công việc không lưu", d: "Khi cập nhật ngày kết thúc, giao diện báo thành công nhưng giá trị cũ vẫn còn sau khi tải lại trang.", r: "Đã sửa bước xác thực dữ liệu ngày và cập nhật lại bản ghi." },
  { s: "Không tải được tệp đính kèm", d: "Tệp PDF dung lượng nhỏ không tải lên được và giao diện hiển thị thông báo thất bại chung.", r: "Đã điều chỉnh cấu hình loại tệp và kiểm tra tải lên thành công." },
  { s: "Bộ lọc trạng thái trả về sai kết quả", d: "Danh sách vẫn chứa bản ghi đã đóng khi chọn bộ lọc chỉ hiển thị trạng thái đang xử lý.", r: "Đã cập nhật điều kiện lọc và xác minh kết quả trên nhiều trạng thái." },
  { s: "Xuất Excel thiếu một số cột", d: "Tệp xuất dữ liệu không có cột đơn vị và người xử lý dù các cột này đang hiển thị trên màn hình.", r: "Đã bổ sung ánh xạ cột cho chức năng xuất dữ liệu." },
  { s: "Thông báo email gửi trùng", d: "Một thay đổi trạng thái tạo ra hai email giống nhau cho cùng một người nhận thử nghiệm.", r: "Đã loại bỏ tác vụ gửi lặp và kiểm tra chỉ còn một thông báo." },
  { s: "Tìm kiếm không nhận ký tự có dấu", d: "Tìm kiếm theo từ khóa tiếng Việt có dấu không trả về bản ghi phù hợp trong khi từ khóa không dấu hoạt động.", r: "Đã chuẩn hóa chuỗi tìm kiếm và xây dựng lại chỉ mục." },
  { s: "Trang danh sách tải chậm", d: "Danh sách khoảng vài trăm bản ghi mất nhiều thời gian để hiển thị khi bật hai điều kiện lọc.", r: "Đã tối ưu truy vấn và giới hạn dữ liệu tải theo trang." },
  { s: "Sai múi giờ trên lịch sử cập nhật", d: "Thời gian trong lịch sử hoạt động lệch so với múi giờ cấu hình của người dùng thử nghiệm.", r: "Đã thống nhất xử lý thời gian theo UTC và chuyển đổi khi hiển thị." },
  { s: "Không thể đặt lại mật khẩu", d: "Liên kết đặt lại mật khẩu hợp lệ nhưng trang xác nhận báo mã đã hết hạn ngay khi mở.", r: "Đã sửa cách kiểm tra thời hạn mã và phát hành liên kết mới." },
  { s: "Mã xác thực hai bước không được chấp nhận", d: "Mã xác thực mới tạo bị từ chối trong khoảng thời gian còn hiệu lực.", r: "Đã đồng bộ thời gian máy chủ và kiểm tra luồng xác thực hai bước." },
  { s: "Vai trò mới chưa áp dụng", d: "Sau khi quản trị viên đổi vai trò, phiên hiện tại vẫn dùng quyền cũ kể cả sau khi tải lại trang.", r: "Đã xóa bộ nhớ đệm quyền và yêu cầu đăng nhập lại." },
  { s: "Bản ghi bị tạo trùng", d: "Nhấn nút lưu một lần nhưng hệ thống tạo hai bản ghi có nội dung giống nhau.", r: "Đã bổ sung khóa chống gửi lặp và hợp nhất bản ghi thử nghiệm." },
  { s: "Không lưu được ghi chú dài", d: "Ghi chú vượt quá độ dài nhất định bị cắt mà không có cảnh báo cho người nhập.", r: "Đã tăng giới hạn trường và bổ sung cảnh báo độ dài." },
  { s: "Sắp xếp theo ngày không chính xác", d: "Danh sách sắp xếp ngày theo chuỗi nên thứ tự ngày và tháng không đúng.", r: "Đã chuyển trường ngày sang kiểu dữ liệu ngày và kiểm tra sắp xếp." },
  { s: "Phê duyệt bị kẹt ở bước trung gian", d: "Yêu cầu đã được người duyệt đầu tiên xác nhận nhưng không chuyển sang người duyệt kế tiếp.", r: "Đã khởi chạy lại luồng và bổ sung kiểm tra chuyển bước." },
  { s: "Không hủy được yêu cầu nháp", d: "Nút hủy không phản hồi đối với yêu cầu chưa gửi và không có người duyệt.", r: "Đã sửa điều kiện trạng thái và xác nhận hủy thành công." },
  { s: "Số liệu tổng hợp không khớp chi tiết", d: "Tổng trên thẻ chỉ số cao hơn một bản ghi so với danh sách chi tiết cùng bộ lọc.", r: "Đã đồng nhất điều kiện tính tổng và truy vấn chi tiết." },
  { s: "Biểu đồ không cập nhật sau khi lọc", d: "Bảng dữ liệu thay đổi theo bộ lọc nhưng biểu đồ vẫn giữ số liệu của lần tải trước.", r: "Đã liên kết lại nguồn biểu đồ với trạng thái bộ lọc hiện tại." },
  { s: "Không mở được bản xem trước tài liệu", d: "Tài liệu đã tải lên thành công nhưng cửa sổ xem trước chỉ hiển thị màn hình trắng.", r: "Đã cập nhật dịch vụ xem trước và tạo lại bản chuyển đổi." },
  { s: "Phiên làm việc hết hạn quá sớm", d: "Phiên đăng nhập kết thúc sớm hơn thời lượng cấu hình dù người dùng vẫn thao tác liên tục.", r: "Đã sửa cơ chế gia hạn phiên khi có hoạt động hợp lệ." },
  { s: "API trả về lỗi giới hạn tần suất", d: "Tích hợp thử nghiệm nhận lỗi giới hạn dù số yêu cầu thấp hơn ngưỡng cấu hình.", r: "Đã điều chỉnh bộ đếm theo khóa ứng dụng và đặt lại giới hạn thử nghiệm." },
  { s: "Webhook không gửi sự kiện cập nhật", d: "Đối tác thử nghiệm không nhận được sự kiện khi trạng thái bản ghi thay đổi.", r: "Đã kích hoạt lại webhook và gửi bù sự kiện kiểm thử." },
  { s: "Đồng bộ danh mục bị thiếu giá trị", d: "Một số giá trị đang hoạt động không xuất hiện trong danh sách chọn sau lần đồng bộ gần nhất.", r: "Đã chạy đồng bộ đầy đủ và làm mới bộ nhớ đệm danh mục." },
  { s: "Không tính được số ngày nghỉ còn lại", d: "Màn hình hiển thị trống cho số ngày nghỉ còn lại khi có điều chỉnh giữa kỳ.", r: "Đã cập nhật quy tắc cộng dồn và tính lại dữ liệu thử nghiệm." },
  { s: "Bảng lương mẫu sai khoản khấu trừ", d: "Khoản khấu trừ một lần bị áp dụng hai lần trong kỳ tính thử nghiệm.", r: "Đã sửa quy tắc phát sinh một lần và tính lại bảng lương mẫu." },
  { s: "Hóa đơn không chuyển sang trạng thái đã thanh toán", d: "Giao dịch thử nghiệm đã hoàn tất nhưng hóa đơn vẫn hiển thị đang chờ xử lý.", r: "Đã đối soát giao dịch và cập nhật luồng chuyển trạng thái." },
  { s: "Không thêm được dòng chi phí", d: "Biểu mẫu từ chối dòng chi phí hợp lệ khi số tiền có phần thập phân.", r: "Đã sửa quy tắc kiểm tra định dạng số tiền." },
  { s: "Mã dự án mẫu không xuất hiện", d: "Dự án đang hoạt động không có trong danh sách chọn khi tạo yêu cầu mới.", r: "Đã cập nhật phạm vi truy vấn dự án và làm mới danh sách." },
  { s: "Phân bổ nguồn lực vượt giới hạn", d: "Hệ thống cho phép tổng tỷ lệ phân bổ vượt mức tối đa mà không cảnh báo.", r: "Đã bổ sung kiểm tra tổng tỷ lệ trước khi lưu." },
  { s: "Không gia hạn được mốc công việc", d: "Ngày gia hạn hợp lệ bị từ chối do hệ thống dùng ngày cũ để kiểm tra.", r: "Đã sửa thứ tự cập nhật và xác thực mốc thời gian." },
  { s: "Thiếu người phụ trách trong danh sách", d: "Một người dùng thử nghiệm đang hoạt động không xuất hiện trong danh sách người phụ trách.", r: "Đã đồng bộ lại danh sách tài khoản và quyền lựa chọn." },
  { s: "Không lưu được thông tin khách hàng mẫu", d: "Biểu mẫu báo thiếu dữ liệu dù tất cả trường bắt buộc đã được nhập bằng dữ liệu giả lập.", r: "Đã sửa ánh xạ trường bắt buộc và lưu lại bản ghi mẫu." },
  { s: "Giai đoạn bán hàng tự chuyển sai", d: "Cơ hội thử nghiệm chuyển sang giai đoạn tiếp theo trước khi hoàn tất điều kiện bắt buộc.", r: "Đã điều chỉnh quy tắc chuyển giai đoạn và chạy lại kiểm thử." },
  { s: "Không tạo được báo giá", d: "Chức năng tạo báo giá dừng tại bước tính thuế cho sản phẩm mẫu.", r: "Đã cập nhật cấu hình thuế và tạo lại báo giá thử nghiệm." },
  { s: "Đơn mua hàng thiếu bước duyệt", d: "Đơn có giá trị vượt ngưỡng nhưng chỉ đi qua một cấp duyệt thay vì hai cấp.", r: "Đã sửa điều kiện ngưỡng và khởi tạo lại luồng duyệt." },
  { s: "Thiết bị không cập nhật trạng thái", d: "Thiết bị kiểm thử đã bàn giao nhưng danh sách tài sản vẫn hiển thị trong kho.", r: "Đã đồng bộ biên bản bàn giao và cập nhật trạng thái tài sản." },
  { s: "Không đặt được phòng họp", d: "Khung giờ trống hiển thị có thể chọn nhưng thao tác đặt chỗ báo xung đột.", r: "Đã làm mới lịch phòng và xử lý bản ghi giữ chỗ hết hạn." },
  { s: "Ứng dụng di động không nhận thông báo", d: "Thiết bị thử nghiệm đã bật thông báo nhưng không nhận cảnh báo trạng thái mới.", r: "Đã đăng ký lại mã thiết bị và gửi thử thông báo thành công." },
  { s: "Màn hình di động bị vỡ bố cục", d: "Các nút thao tác chồng lên phần mô tả khi mở trên màn hình nhỏ.", r: "Đã điều chỉnh bố cục đáp ứng và kiểm tra trên nhiều kích thước." },
  { s: "Không đổi được ngôn ngữ giao diện", d: "Lựa chọn ngôn ngữ được lưu nhưng giao diện quay về ngôn ngữ mặc định sau khi đăng nhập lại.", r: "Đã lưu tùy chọn theo hồ sơ và kiểm tra lại phiên mới." },
  { s: "Tệp CSV nhập vào sai mã hóa", d: "Nội dung tiếng Việt trong tệp CSV hiển thị sai ký tự sau khi nhập.", r: "Đã chuẩn hóa mã hóa UTF-8 và nhập lại tệp mẫu." },
  { s: "Dữ liệu nhập hàng loạt thiếu cảnh báo", d: "Một số dòng không hợp lệ bị bỏ qua nhưng báo cáo kết quả không nêu rõ nguyên nhân.", r: "Đã bổ sung tệp lỗi theo từng dòng và thông báo tổng hợp." },
  { s: "Bản sao lưu thử nghiệm không hoàn tất", d: "Tác vụ sao lưu dừng giữa chừng và không tạo tệp kết quả trong kho lưu trữ thử nghiệm.", r: "Đã giải phóng dung lượng tạm và chạy lại tác vụ sao lưu." },
  { s: "Không khôi phục được cấu hình", d: "Tệp cấu hình sao lưu hợp lệ bị từ chối khi khôi phục trên môi trường kiểm thử.", r: "Đã cập nhật bộ kiểm tra phiên bản và khôi phục thành công." },
  { s: "Cảnh báo bảo mật không được ghi nhận", d: "Một lần đăng nhập thất bại liên tiếp không tạo sự kiện trong nhật ký cảnh báo.", r: "Đã sửa quy tắc phát hiện và xác minh sự kiện được ghi nhận." },
  { s: "Nhật ký hoạt động thiếu thao tác cập nhật", d: "Lịch sử chỉ ghi thời điểm tạo mà không ghi lần thay đổi trường quan trọng.", r: "Đã bổ sung sự kiện kiểm toán cho thao tác cập nhật." },
  { s: "Không thể tải báo cáo PDF", d: "Tác vụ tạo PDF hoàn tất nhưng liên kết tải xuống trả về tệp rỗng.", r: "Đã sửa bước lưu tệp và tạo lại báo cáo mẫu." },
  { s: "Cột báo cáo hiển thị sai định dạng", d: "Giá trị phần trăm được hiển thị như số nguyên trong báo cáo tải xuống.", r: "Đã áp dụng đúng định dạng phần trăm cho cột báo cáo." },
  { s: "Điều kiện SLA không kích hoạt", d: "Ticket thử nghiệm vượt thời hạn nhưng chưa được đánh dấu quá hạn.", r: "Đã sửa mốc tính SLA và chạy lại tác vụ đánh giá." },
  { s: "Không chuyển được người xử lý", d: "Ticket đang mở không thể chuyển sang nhóm hỗ trợ khác dù nhóm đích đang hoạt động.", r: "Đã cập nhật quyền chuyển nhóm và kiểm tra lịch sử phân công." },
  { s: "Mẫu email hiển thị sai biến", d: "Email thử nghiệm hiển thị tên biến thay vì giá trị trạng thái của ticket.", r: "Đã sửa cú pháp biến trong mẫu và gửi lại email thử nghiệm." },
  { s: "Không tải thêm dữ liệu khi cuộn", d: "Danh sách dừng ở trang đầu khi người dùng cuộn xuống cuối màn hình.", r: "Đã sửa tham số phân trang và kiểm tra tải liên tục." },
  { s: "Bộ nhớ đệm giữ dữ liệu cũ", d: "Sau khi cập nhật, một số người dùng vẫn thấy giá trị cũ trong vài phiên liên tiếp.", r: "Đã điều chỉnh khóa bộ nhớ đệm và chủ động làm mới dữ liệu." },
  { s: "Không nhận dữ liệu từ hệ thống tích hợp", d: "Luồng tích hợp hoàn tất nhưng không tạo bản ghi mới ở hệ thống đích.", r: "Đã sửa ánh xạ trường bắt buộc và chạy lại gói dữ liệu mẫu." },
  { s: "Kết nối dịch vụ bị ngắt quãng", d: "Yêu cầu tới dịch vụ phụ trợ thỉnh thoảng hết thời gian chờ trong giờ cao điểm thử nghiệm.", r: "Đã bổ sung cơ chế thử lại có giới hạn và tối ưu thời gian chờ." },
  { s: "Trường bắt buộc không được đánh dấu", d: "Biểu mẫu cho phép gửi khi một trường nghiệp vụ quan trọng đang để trống.", r: "Đã bổ sung kiểm tra phía giao diện và máy chủ." },
  { s: "Thông báo lỗi không rõ nguyên nhân", d: "Thao tác lưu thất bại nhưng chỉ hiển thị mã lỗi chung, khó xác định trường cần sửa.", r: "Đã bổ sung thông báo theo từng lỗi xác thực." },
];

const contexts = [
  "trên môi trường kiểm thử",
  "sau lần cập nhật gần nhất",
  "khi sử dụng dữ liệu mẫu",
  "ở luồng xử lý tiêu chuẩn",
  "với vai trò người dùng thông thường",
  "khi thao tác trên trình duyệt mới",
  "trong lần kiểm tra định kỳ",
  "ở phiên làm việc đầu ngày",
];

function commentsFor(issue, i) {
  const parts = [
    "<p>Đã tiếp nhận ticket và xác nhận hiện tượng bằng dữ liệu giả lập.</p>",
    `<p>${issue.r}</p>`,
  ];
  if (i % 3 === 0) parts.push("<p>Đã kiểm tra lại cùng người dùng mẫu; kết quả hoạt động bình thường.</p>");
  if (i % 7 === 0) parts.push("<p>Ticket được đóng sau khi hoàn tất bước xác minh cuối.</p>");
  return parts.map((part, idx) => `[Comment ${idx + 1}] ${part}`).join("\n-----\n");
}

function makeRow(index, app, kind) {
  const issue = issues[(index * 17 + (kind === "new" ? 11 : 0)) % issues.length];
  const context = contexts[(index * 5) % contexts.length];
  const commentText = commentsFor(issue, index);
  const commentCount = (commentText.match(/\[Comment /g) ?? []).length;
  const fetchedAt = new Date(Date.UTC(2026, 0, 5, 1 + ((index * 6) % 22), (index * 13) % 60, 0));
  fetchedAt.setUTCDate(fetchedAt.getUTCDate() + Math.floor(index / 4));
  return [
    `${kind === "new" ? "TKT-SYN" : "TKT-ANON"}-${String(index + 1).padStart(4, "0")}`,
    app,
    `${issue.s} ${context}`,
    `${issue.d} Phạm vi xử lý thuộc ${app}; không sử dụng dữ liệu người thật hoặc thông tin sản xuất.`,
    commentCount,
    commentText,
    "OK",
    fetchedAt,
  ];
}

const rows = [];
for (let i = 0; i < sourceRows.length; i += 1) {
  const originalApp = String(sourceRows[i]?.[1] ?? "");
  const sanitizedApp = appMap.get(originalApp) ?? baseApps[i % 7];
  rows.push(makeRow(i, sanitizedApp, "anon"));
}
for (let i = 0; i < 500; i += 1) {
  const rowIndex = sourceRows.length + i;
  rows.push(makeRow(rowIndex, baseApps[(i * 7 + 3) % baseApps.length], "new"));
}

const workbook = Workbook.create();
const sheet = workbook.worksheets.add("Tickets");
sheet.showGridLines = false;
sheet.freezePanes.freezeRows(1);

const headers = [[
  "internal_ticket_id",
  "app",
  "summary",
  "description",
  "comment_count",
  "comment_text",
  "fetch_status",
  "fetched_at",
]];
sheet.getRange("A1:H1").values = headers;
sheet.getRange("A2").write(rows);

const lastRow = rows.length + 1;
const usedRange = sheet.getRange(`A1:H${lastRow}`);
usedRange.format.font = { name: "Arial", size: 10, color: "#1F2937" };
usedRange.format.verticalAlignment = "center";

const header = sheet.getRange("A1:H1");
header.format = {
  fill: "#176B4D",
  font: { name: "Arial", size: 10, bold: true, color: "#FFFFFF" },
  horizontalAlignment: "center",
  verticalAlignment: "center",
  wrapText: true,
  borders: { preset: "inside", style: "thin", color: "#D7E7DF" },
};
header.format.rowHeight = 28;

sheet.getRange(`A2:A${lastRow}`).format.horizontalAlignment = "center";
sheet.getRange(`B2:D${lastRow}`).format.horizontalAlignment = "left";
sheet.getRange(`C2:D${lastRow}`).format.wrapText = true;
sheet.getRange(`E2:E${lastRow}`).format.horizontalAlignment = "center";
sheet.getRange(`F2:F${lastRow}`).format.wrapText = false;
sheet.getRange(`G2:G${lastRow}`).format.horizontalAlignment = "center";
sheet.getRange(`H2:H${lastRow}`).format.horizontalAlignment = "center";
sheet.getRange(`H2:H${lastRow}`).format.numberFormat = "yyyy-mm-dd hh:mm:ss";
sheet.getRange(`A2:H${lastRow}`).format.rowHeight = 44;

sheet.getRange(`A1:A${lastRow}`).format.columnWidth = 18;
sheet.getRange(`B1:B${lastRow}`).format.columnWidth = 22;
sheet.getRange(`C1:C${lastRow}`).format.columnWidth = 42;
sheet.getRange(`D1:D${lastRow}`).format.columnWidth = 58;
sheet.getRange(`E1:E${lastRow}`).format.columnWidth = 14;
sheet.getRange(`F1:F${lastRow}`).format.columnWidth = 58;
sheet.getRange(`G1:G${lastRow}`).format.columnWidth = 14;
sheet.getRange(`H1:H${lastRow}`).format.columnWidth = 22;

const table = sheet.tables.add(`A1:H${lastRow}`, true, "TicketsTable");
table.style = "TableStyleMedium4";
table.showBandedColumns = false;
table.showFilterButton = true;

workbook.recalculate();

const keyCheck = await workbook.inspect({
  kind: "table",
  range: "Tickets!A1:H8",
  include: "values,formulas",
  tableMaxRows: 8,
  tableMaxCols: 8,
  maxChars: 12000,
});
console.log("KEY_CHECK");
console.log(keyCheck.ndjson);

const errors = await workbook.inspect({
  kind: "match",
  searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!|#SPILL!|#CALC!",
  options: { useRegex: true, maxResults: 300 },
  summary: "final formula error scan",
});
console.log("ERROR_SCAN");
console.log(errors.ndjson);

const sensitive = await workbook.inspect({
  kind: "match",
  searchTerm: "cmcglobal|@cmc|https?://|data-email=|10\\.|172\\.(1[6-9]|2[0-9]|3[01])\\.|192\\.168\\.|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}",
  options: { useRegex: true, maxResults: 300, caseSensitive: false },
  summary: "sensitive data scan",
});
console.log("SENSITIVE_SCAN");
console.log(sensitive.ndjson);

const preview = await workbook.render({
  sheetName: "Tickets",
  range: "A1:H12",
  scale: 1.5,
  format: "png",
});

await fs.mkdir(outputDir, { recursive: true });
await fs.writeFile(previewPath, new Uint8Array(await preview.arrayBuffer()));
const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(outputPath);

const exported = await SpreadsheetFile.importXlsx(await FileBlob.load(outputPath));
const exportCheck = await exported.inspect({
  kind: "workbook,sheet,table",
  maxChars: 6000,
  tableMaxRows: 3,
  tableMaxCols: 8,
});
console.log("EXPORT_CHECK");
console.log(exportCheck.ndjson);
console.log(JSON.stringify({ outputPath, previewPath, sourceRows: sourceRows.length, addedRows: 500, totalRows: rows.length }));
