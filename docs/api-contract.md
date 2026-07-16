# NotificationService API contract

Tài liệu này là contract thực thi cho hai caller nội bộ. Các giá trị ID lưu
trong database là `bigint`; caller REST phải dùng serializer hỗ trợ `Int64`.
GraphQL dùng scalar `ID` để tránh giới hạn 32-bit của GraphQL `Int`.

## 1. REST: Social Graph tạo notification

`POST /internal/notifications`

| Header | Bắt buộc | Ý nghĩa |
| --- | --- | --- |
| `X-Internal-NotificationService-Secret` | Có | Shared secret giữa Social Graph và NotificationService |
| `Idempotency-Key` | Có | Opaque, non-empty key đại diện cho một business event |
| `Content-Type: application/json` | Có | Payload JSON |

Request body:

```json
{
  "creatorId": 1001,
  "receiverId": 2002,
  "actionType": 1,
  "objectId": 3003
}
```

| Field | Type | Quy tắc |
| --- | --- | --- |
| `creatorId` | signed 64-bit integer | Dương; actor tạo action |
| `receiverId` | signed 64-bit integer | Dương; user nhận notification |
| `actionType` | integer | `0=LIKE`, `1=COMMENT`, `2=TAG`, `3=MENTION`, `4=FRIEND_REQUEST`, `5=FRIEND_ACCEPT`, `6=GROUP_INVITE`, `7=GROUP_JOIN_REQUEST`, `8=GROUP_JOIN_ACCEPTED`, `9=SHARE` |
| `objectId` | signed 64-bit integer | Dương; object đích của action |

Response `201 Created` (lần đầu) hoặc `200 OK` (idempotent replay):

```json
{
  "id": 744000000000000001,
  "creatorId": 1001,
  "receiverId": 2002,
  "actionType": 1,
  "objectId": 3003,
  "createdAt": "2026-07-14T15:00:00Z",
  "isRead": false
}
```

| Status | Khi nào |
| --- | --- |
| `400` | Body/header Idempotency-Key thiếu hoặc dữ liệu không hợp lệ |
| `401` | Internal secret thiếu hoặc không khớp |
| `409` | Idempotency-Key đã dùng cho payload khác |
| `500` | Lỗi không mong đợi; caller có thể retry bằng cùng key |

Một event subscription chỉ được phát sau commit thành công của lần tạo đầu
tiên. Replay idempotent trả response cũ nhưng không phát lại event.

## 2. GraphQL: Gateway đọc và cập nhật feed

HTTP `POST /graphql`, bao gồm GraphQL-over-SSE với `Accept: text/event-stream`, đều cần:

```http
X-Gateway-Secret: <gateway secret>
X-User-Id: <positive decimal Int64>
```

`X-User-Id` là nguồn danh tính duy nhất. Caller không được truyền
`receiverId`/`userId` qua GraphQL; server bỏ qua mọi user identity không đáng tin
cậy.

### Schema có thể gọi qua Gateway

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

### Fetch feed

```graphql
query Notifications($first: Int!, $after: String) {
  notifications(first: $first, after: $after) {
    nodes {
      id
      creatorId
      receiverId
      actionType
      objectId
      createdAt
      isRead
    }
    pageInfo { hasNextPage endCursor }
    unreadCount
  }
}
```

`first` mặc định 20, server chấp nhận 1–50. Feed sắp xếp `createdAt DESC, id
DESC`; `after` là cursor opaque của item cuối trang trước.

### Đánh dấu đã đọc

```graphql
mutation Read($id: ID!) {
  markNotificationRead(id: $id) { id isRead }
}
```

Mutation chỉ tác động notification của `X-User-Id`. ID lạ/không thuộc
user trả `null`; mutation idempotent khi row đã được read.

```graphql
mutation ReadAll {
  markAllNotificationsRead
}
```

Giá trị trả về là số notification của trusted user đã đổi từ unread sang read.

### Subscription realtime

```graphql
subscription NotificationCreated {
  notificationCreated {
    id
    creatorId
    receiverId
    actionType
    objectId
    createdAt
    isRead
  }
}
```

Gateway phải gửi hai trusted headers trên HTTP SSE handshake. Service chốt
user context lúc kết nối và chỉ deliver event có `receiverId` bằng user đó.
Không có argument để subscribe user khác.

Provider subscription v1 là in-memory; chạy một NotificationService instance
cho realtime. Cần provider phân tán/outbox trước khi triển khai nhiều replica.

## 3. Federation

Service đăng ký Apollo Federation v1 subgraph (`Federation10`) và expose
`Notification` entity với key `id`. Federation entity resolution không được mở quyền đọc arbitrary
notification: user-facing query/mutation/subscription luôn kiểm tra trusted
header context.
