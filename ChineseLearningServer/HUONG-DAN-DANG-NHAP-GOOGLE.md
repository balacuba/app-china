# Bật "Đăng nhập bằng Google" (kèm Gemini không cần API key) cho máy chủ học

Nút **Đăng nhập bằng Google** chỉ hiện khi máy chủ có *Client ID* do Google cấp. Thêm *Client secret* thì khi đăng nhập Google, người học được hỏi luôn quyền dùng Gemini, và trang **Dịch đoạn văn** (web và app) dùng Gemini bằng chính tài khoản Google đó, **không cần API key**. Tất cả tạo miễn phí trong Google Cloud, chỉ làm một lần.

## 1. Tạo Client ID và Client secret

1. Mở <https://console.cloud.google.com/> và đăng nhập Google.
2. Tạo project mới (ví dụ `hoc-tieng-trung`).
3. Vào **APIs & Services → Library**, tìm **Generative Language API** → **Enable** (để dùng Gemini qua tài khoản Google).
4. Vào **APIs & Services → OAuth consent screen** (Google Auth Platform):
   - User type: **External** → Create.
   - App name: `Học tiếng Trung`, User support email và Developer contact: Gmail của bạn → Save.
   - Ở **Audience → Test users**, thêm các Gmail sẽ dùng (khi app còn ở chế độ Testing chỉ các Gmail này đăng nhập được).
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

Chỉ có Client ID mà không có Client secret thì vẫn đăng nhập Google được, nhưng Dịch đoạn văn vẫn cần API key.

## 3. Dùng

- **Web:** bấm **Đăng nhập bằng Google**, chọn Gmail, bấm **Tiếp tục/Cho phép** ở màn hình Google hỏi quyền (trong đó có quyền dùng Gemini). Lần đầu máy chủ tự tạo tài khoản mang tên chính Gmail đó. Vào **Dịch đoạn văn** sẽ thấy “Dùng Gemini qua tài khoản Google của bạn, không cần API key”.
- **App desktop:** Cài đặt → Đồng bộ với web → **Đăng nhập bằng Google**. App mở trình duyệt; đăng nhập Google (nếu chưa) rồi bấm **Cho phép**. App tự nhận đăng nhập, đồng bộ, và Dịch đoạn văn trong app cũng dùng Gemini qua máy chủ (máy chủ phải đang chạy).
- Đã có API key thì vẫn dùng được: khi Gemini qua tài khoản Google không dùng được (đăng nhập bằng tên/mật khẩu, chưa cho quyền, máy chủ tắt…), web và app tự quay về dùng key.

## Giới hạn

- Google chỉ cho đăng nhập trên trang `http://localhost…` hoặc trang `https://` có tên miền. Mở web từ điện thoại qua địa chỉ IP trong mạng nhà (`http://192.168.x.x:5080`) thì **không** dùng được nút Google; khi đó đăng nhập bằng tên và mật khẩu.
- Khi app trên Google Cloud còn ở chế độ **Testing**: chỉ các Gmail trong Test users đăng nhập được, và quyền Gemini hết hạn sau **7 ngày** (lúc đó trang báo cần đăng nhập lại bằng Google). Muốn bỏ giới hạn này phải **Publish app**; vì quyền `cloud-platform` là quyền nhạy cảm, Google có thể yêu cầu xác minh app.
- Lượt dùng Gemini tính vào project Google Cloud của máy chủ (`hoc-tieng-trung`), theo hạn mức miễn phí của Google.
- Tài khoản tạo bằng Google không có mật khẩu: luôn đăng nhập bằng Google.
