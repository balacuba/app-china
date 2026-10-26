# Bật "Đăng nhập bằng Google" (kèm API key Gemini tự động) cho máy chủ học

Nút **Đăng nhập bằng Google** chỉ hiện khi máy chủ có *Client ID* do Google cấp. Thêm *Client secret* thì khi đăng nhập Google, người học được hỏi luôn quyền dùng Gemini, và máy chủ **tự tạo API key Gemini miễn phí trong chính tài khoản Google đó** (như Google AI Studio làm). Trang **Dịch đoạn văn** (web và app) dùng key này; người học **không phải nhập key**. Tất cả tạo miễn phí trong Google Cloud, chỉ làm một lần.

## 1. Tạo Client ID và Client secret

1. Mở <https://console.cloud.google.com/> và đăng nhập Google.
2. Tạo project mới (ví dụ `hoc-tieng-trung`).
3. Vào **APIs & Services → Library**, bật (**Enable**) lần lượt: **Generative Language API**, **Cloud Resource Manager API**, **Service Usage API** và **API Keys API** (để máy chủ tạo được API key Gemini trong tài khoản của người học).
4. Vào **APIs & Services → OAuth consent screen** (Google Auth Platform):
   - User type: **External** → Create.
   - App name: `Học tiếng Trung`, User support email và Developer contact: Gmail của bạn → Save.
   - Ở **Data access → Add or remove scopes**, thêm `.../auth/userinfo.email`, `.../auth/userinfo.profile` (để lấy tên trên Google làm tên tài khoản), `openid`, `.../auth/cloud-platform` và `.../auth/generative-language.retriever` → Save.
   - Ở **Audience**, bấm **Publish app** → Confirm (Publishing status: **In production**). Từ đó **mọi Gmail** đều đăng nhập được, không cần thêm vào Test users, và quyền Gemini không còn hết hạn sau 7 ngày. Xem mục *Giới hạn* bên dưới.
5. Vào **Clients → Create client** (hoặc **Credentials → Create credentials → OAuth client ID**):
   - Application type: **Web application**.
   - **Authorized JavaScript origins**: `http://localhost:5080` và `http://localhost`.
   - **Authorized redirect URIs**: `http://localhost:5080/api/oauth/google/callback`.
   - **Create**.
6. Sao chép **Client ID** (dạng `1234567890-xxxx.apps.googleusercontent.com`) và **Client secret** (dạng `GOCSPX-…`).
   Client secret là bí mật: không gửi cho ai, không đưa lên GitHub. Nếu lỡ để lộ (ví dụ trong ảnh chụp màn hình), vào client đó bấm **Reset secret** để lấy secret mới.

## 2. Đưa Client ID và Client secret vào máy chủ

**Bản đã cài bằng bộ cài:** tạo file `%LOCALAPPDATA%\ChineseLearningApp\server\settings.json` (cùng thư mục với `app-data.db`) với nội dung:

```json
{
  "GoogleClientId": "1234567890-xxxx.apps.googleusercontent.com",
  "GoogleClientSecret": "GOCSPX-..."
}
```

File này nằm ngoài thư mục cài đặt nên cài lại bản mới không mất.

**Khi chạy từ mã nguồn** (`dotnet run --project ChineseLearningServer`): tạo `settings.json` như trên trong thư mục `ChineseLearningServer` (file này không được đưa lên git). Khi đóng gói bằng `installer/build-installer.ps1`, file đó được chép kèm bộ cài.

Tắt máy chủ rồi bật lại. Trang đăng nhập sẽ có nút **Đăng nhập bằng Google**.

Chỉ có Client ID mà không có Client secret thì vẫn đăng nhập Google được, nhưng không dùng được Dịch đoạn văn.

## 3. Dùng

- **Web:** bấm **Đăng nhập bằng Google**, chọn Gmail, đánh dấu cả quyền Gemini (Google Cloud) rồi bấm **Tiếp tục** ở màn hình Google hỏi quyền. Lần đầu máy chủ tự tạo tài khoản mang **tên trên Google** (ví dụ “Lại Hoài”; trùng tên người khác thì thành “Lại Hoài 2”). Tài khoản cũ đang mang tên là địa chỉ Gmail sẽ tự đổi sang tên Google ở lần đăng nhập Google kế tiếp. Vào **Dịch đoạn văn**: lần đầu trang tự lấy API key (khoảng 1 phút; Google Cloud của người học có thêm project `hoc-tieng-trung-xxxxxx`), sau đó báo “Gemini sẵn sàng”. Bỏ đánh dấu quyền Gemini thì vẫn đăng nhập được, nhưng muốn dùng Dịch đoạn văn phải đăng nhập Google lại và đánh dấu quyền đó.
- **App desktop:** Cài đặt → Đồng bộ với web → **Đăng nhập bằng Google**. App mở trình duyệt; đăng nhập Google (nếu chưa) rồi bấm **Cho phép**. App tự nhận đăng nhập, đồng bộ, rồi tự lấy API key Gemini của người học; Dịch đoạn văn trong app dùng ngay key đó.
- Không còn ô nhập API key bằng tay. Khi chưa tạo được key riêng, web và app tạm hỏi Gemini qua máy chủ bằng quyền Google của người học. Tài khoản đăng nhập bằng tên/mật khẩu không dùng được Dịch đoạn văn.
- Nếu lần đầu tạo key Google báo tài khoản **chưa đồng ý điều khoản Google Cloud**: người học mở <https://console.cloud.google.com/> một lần, đồng ý điều khoản, rồi mở lại Dịch đoạn văn.

## Giới hạn

- Google chỉ cho đăng nhập trên trang `http://localhost…` hoặc trang `https://` có tên miền. Mở web từ điện thoại qua địa chỉ IP trong mạng nhà (`http://192.168.x.x:5080`) thì **không** dùng được nút Google; khi đó đăng nhập bằng tên và mật khẩu.
- Khi app trên Google Cloud còn ở chế độ **Testing**: chỉ các Gmail trong Test users đăng nhập được, và quyền Gemini hết hạn sau **7 ngày**. Bấm **Publish app** (bước 1) để bỏ giới hạn này.
- Sau khi Publish mà chưa được Google xác minh: vì quyền Gemini (`cloud-platform`) là quyền nhạy cảm, màn hình Google hiện **“Google hasn't verified this app”**. Người dùng bấm **Advanced → Go to Học tiếng Trung (unsafe)** để tiếp tục. Google cũng giới hạn app chưa xác minh ở **100 người dùng** có quyền nhạy cảm. Muốn bỏ cả cảnh báo và giới hạn 100 người thì gửi xác minh ở **Verification Center** (cần tên miền, trang chính sách quyền riêng tư và video demo).
- Dùng bằng API key tự tạo thì lượt Gemini tính vào project riêng của từng người học (mỗi người một hạn mức miễn phí). Chỉ khi tạm hỏi qua máy chủ, lượt mới tính vào project Google Cloud của máy chủ.
- Tài khoản tạo bằng Google không có mật khẩu: luôn đăng nhập bằng Google.
