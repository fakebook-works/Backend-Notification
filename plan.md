# Kế hoạch triển khai NotificationService

## Mục tiêu

Xây dựng NotificationService .NET 8 làm Apollo Federation v1 subgraph. Gateway là
điểm vào GraphQL duy nhất; Social Graph tạo thông báo qua REST nội bộ. Service
lưu notification trong PostgreSQL, trả feed qua GraphQL và phát thông báo mới
qua GraphQL subscription thời gian thực.

## Kiến trúc và ranh giới truy cập

- **Gateway → NotificationService:** GraphQL HTTP và GraphQL-over-SSE tại `/graphql`.
  Mỗi request hoặc SSE handshake bắt buộc có
  `X-Gateway-Secret` và `X-User-Id`.
- **Social Graph → NotificationService:** REST nội bộ `POST
  /internal/notifications`, bắt buộc có
  `X-Internal-NotificationService-Secret` và `Idempotency-Key`.
- Client không gọi service trực tiếp. GraphQL không nhận `userId` hoặc
  `receiverId` từ arguments; danh tính luôn lấy từ header đáng tin cậy do
  Gateway gửi.
- Service không gọi ngược Gateway. Gateway compose schema Apollo Federation v1, forward
  trusted headers và duy trì luồng `text/event-stream` cho subscription.

## Cấu hình bắt buộc

Đọc cấu hình từ `appsettings`/environment variables, không hard-code hoặc log
secret:

| Key | Mục đích |
| --- | --- |
| `ConnectionStrings:NotificationDb` | PostgreSQL connection string |
| `InternalAuthentication:GatewaySecret` | Secret xác thực Gateway cho GraphQL HTTP và SSE |
| `InternalAuthentication:NotificationServiceSecret` | Secret xác thực Social Graph REST |
| `Snowflake:NodeId` | Node id duy nhất của instance tạo ID |

Validate khi khởi động: connection string, hai secret không rỗng và `NodeId`
nằm trong dải Snowflake được hỗ trợ. Trong production, các key trên được map từ
environment variables/secrets manager.

Sau khi đặt cấu hình, cài `dotnet-ef` 8.0.x nếu máy chưa có, rồi chạy migration
bằng `dotnet ef database update --project NotificationService/NotificationService.csproj`.
Chạy service bằng `dotnet run --project NotificationService/NotificationService.csproj`.
Không tự động apply migration khi application startup.

## Dữ liệu và persistence

- Dùng EF Core + Npgsql, migration ban đầu và PostgreSQL.
- Bảng `notification` dùng schema trong `NotificationService/database.md`, với
  tên chuẩn `receiver_id` (không dùng lỗi chính tả `reciver_id`).
- Cột: `id bigint PK`, `creator_id bigint NOT NULL`, `receiver_id bigint NOT
  NULL`, `action_type smallint NOT NULL`, `object_id bigint NOT NULL`,
  `created_at timestamptz NOT NULL`, `is_read boolean NOT NULL DEFAULT false`,
  `idempotency_key` unique/not null.
- `id` do service tạo bằng Snowflake. Generator phải tạo ID duy nhất/đơn điệu
  trong một node, chờ sequence tick mới khi sequence đầy và xử lý clock rollback
  mà không sinh trùng ID.
- Action type: `LIKE=0`, `COMMENT=1`, `TAG=2`, `MENTION=3`,
  `FRIEND_REQUEST=4`, `FRIEND_ACCEPT=5`, `GROUP_INVITE=6`,
  `GROUP_JOIN_REQUEST=7`, `GROUP_JOIN_ACCEPTED=8`, `SHARE=9`.
- Tạo index feed `(receiver_id, created_at DESC, id DESC)` và index unread theo
  `receiver_id` (partial index với `is_read = false` nếu migration hỗ trợ).
- Pagination dùng keyset cursor opaque mã hóa `(created_at, id)`, thứ tự mới
  nhất trước. Không dùng offset.

## REST nội bộ

### `POST /internal/notifications`

Chỉ Social Graph gọi endpoint này.

Headers:

```http
X-Internal-NotificationService-Secret: <internal secret>
Idempotency-Key: <non-empty opaque key>
Content-Type: application/json
```

Body (`long` là JSON number 64-bit trong giao tiếp service-to-service):

```json
{
  "creatorId": 1001,
  "receiverId": 2002,
  "actionType": 0,
  "objectId": 3003
}
```

- `creatorId`, `receiverId`, `objectId` phải là `long` dương; `actionType` là
  một trong `0..9`.
- Secret thiếu/sai trả `401`; header idempotency thiếu trả `400`; payload sai
  trả `400`.
- Key mới: insert trong transaction, commit rồi trả `201 Created` với
  notification đã tạo.
- Cùng key và cùng payload: không tạo record/event mới, trả `200 OK` với record
  cũ. Cùng key nhưng payload khác trả `409 Conflict`.
- Chỉ sau khi transaction commit thành công mới publish subscription event tới
  topic của `receiverId`. Lỗi insert/transaction không được phát event.

Response tạo/replay dùng dạng:

```json
{
  "id": 744000000000000001,
  "creatorId": 1001,
  "receiverId": 2002,
  "actionType": 0,
  "objectId": 3003,
  "createdAt": "2026-07-14T15:00:00Z",
  "isRead": false
}
```

## GraphQL Federation contract

Expose Apollo Federation v1-compatible `Notification` entity với `id: ID!`; ID lớn được
serialize bằng GraphQL `ID`, không phải GraphQL `Int`. Gateway truyền
`X-User-Id` là decimal `long` dương, sau đó resolver dùng giá trị này
cho mọi authorization/filter.

```graphql
enum NotificationActionType {
  LIKE
  COMMENT
  TAG
  MENTION
  FRIEND_REQUEST
  FRIEND_ACCEPT
  GROUP_INVITE
  GROUP_JOIN_REQUEST
  GROUP_JOIN_ACCEPTED
  SHARE
}

type Notification @key(fields: "id") {
  id: ID!
  creatorId: ID!
  receiverId: ID!
  actionType: NotificationActionType!
  objectId: ID!
  createdAt: DateTime!
  isRead: Boolean!
}

type NotificationEdge {
  cursor: String!
  node: Notification!
}

type NotificationPageInfo {
  hasNextPage: Boolean!
  endCursor: String
}

type NotificationConnection {
  nodes: [Notification!]!
  edges: [NotificationEdge!]!
  pageInfo: NotificationPageInfo!
  unreadCount: Int!
}

type Query {
  notifications(first: Int = 20, after: String): NotificationConnection!
}

type Mutation {
  markNotificationRead(id: ID!): Notification
  markAllNotificationsRead: Int!
}

type Subscription {
  notificationCreated: Notification!
}
```

Behavior:

- `notifications` chỉ trả các row có `receiver_id` bằng trusted current user.
  `first` phải nằm trong giới hạn server-defined (1–50); cursor
  malformed trả GraphQL validation error.
- `unreadCount` là tổng notification `is_read=false` của user hiện tại, không
  chỉ số item của trang.
- `markNotificationRead` chỉ cập nhật notification của current user. ID không
  tồn tại hoặc thuộc user khác trả `null` để không lộ ownership.
  Gọi lại với row đã read là idempotent và trả row hiện tại.
- `markAllNotificationsRead` chỉ cập nhật row unread của current user và trả số
  row đã chuyển trạng thái. Không broadcast read-state trong v1.
- `notificationCreated` tự lọc theo trusted current user; không có argument
  receiver ID và payload là notification vừa commit đầy đủ.
- Subscription provider hiện là in-memory, nên realtime v1 phải chạy một instance
  NotificationService. Triển khai provider phân tán/outbox trước khi scale ngang.

GraphQL HTTP và SSE đều fail trước resolver nếu thiếu/sai Gateway secret hoặc
user-id không parse/không dương. Secret so sánh constant-time; danh tính được
chốt tại SSE handshake và áp dụng cho toàn bộ lifetime của stream.

## Tổ chức implementation

- Bỏ WeatherForecast sample; đăng ký controllers, EF DbContext, Snowflake ID
  generator, REST service, GraphQL Federation, subscriptions và Swagger cho
  REST nội bộ.
- Tách các concern: entity/enum + DbContext/migration; create/read domain
  service; authentication middleware/current-user accessor; REST controller;
  GraphQL Query/Mutation/Subscription types.
- Federation entity resolver theo `id` chỉ được Gateway dùng để compose/resolve;
  không được dùng để bypass ownership rules cho user-facing feed/mutations.
- Thêm examples có header vào `NotificationService.http`; giữ Swagger là tài
  liệu/debug endpoint REST nội bộ, không phải public API.
- Cập nhật `NotificationService/database.md` để phản ánh schema cuối cùng, REST
  create contract, enum và GraphQL query/mutation/subscription (bao gồm tên
  `receiver_id` chuẩn).

## Kiểm thử và tiêu chí nghiệm thu

- Unit: Snowflake uniqueness/order/clock rollback, enum mapping, cursor
  encode/decode và input validation.
- Integration với PostgreSQL: migration, create, idempotency replay/conflict,
  pagination ổn định, unread count, ownership/read mutations.
- Security: REST từ chối secret sai/thiếu; GraphQL HTTP và SSE từ chối
  Gateway header sai/thiếu; không thể lấy hoặc mark notification của user khác.
- Subscription: chỉ receiver đúng nhận event; payload đầy đủ; retry REST không
  phát trùng; transaction thất bại không phát event.
- Chạy `dotnet build` và toàn bộ test suite. Xác nhận Gateway Federation compose
  được schema và subscription hoạt động qua SSE với trusted headers.
- Test hiện có chạy bằng `dotnet test
  NotificationService.Tests/NotificationService.Tests.csproj --no-restore`.

## Ngoài phạm vi v1

Không triển khai notification delete, preferences, push mobile, email, batch
fan-out, broadcast trạng thái read hoặc direct public client access. Các tính
năng đó cần contract và authorization riêng trước khi thêm.
