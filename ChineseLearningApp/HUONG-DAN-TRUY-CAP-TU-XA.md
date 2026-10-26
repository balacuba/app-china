# Mở trang web học từ máy khác và từ xa (NAT)

Từ bản 1.6.0, app **Học tiếng Trung** chạy luôn trang web học bên trong nó. Mở app là trang web chạy, tắt app là trang web tắt. Không cần mở máy chủ riêng.

Vào **Cài đặt → Trang web học** để xem trạng thái và địa chỉ.

## 1. Trên máy này

Bấm **🌐 Mở trang web**, hoặc mở trình duyệt vào `http://localhost:5080`.

## 2. Điện thoại, máy khác cùng Wi-Fi

1. Trong thẻ **Trang web học**, để bật ô **Cho máy khác mở trang web** (mặc định đã bật).
2. Bấm **🛡 Mở cổng tường lửa** rồi bấm **Yes** khi Windows hỏi quyền quản trị. Chỉ cần làm một lần.
   Lần đầu app chạy, Windows cũng có thể tự hỏi "Allow access": chọn **Allow** (nên tích cả mạng Private).
3. Trên điện thoại, mở địa chỉ ở dòng **Trong mạng nhà (Wi-Fi/LAN)**, ví dụ `http://192.168.1.10:5080`.

## 3. Từ xa qua Internet (mở cổng NAT trên router)

1. **Giữ cho máy này luôn một địa chỉ trong mạng nhà:** trong trang quản trị router (thường là `http://192.168.1.1` hoặc `192.168.0.1`, tài khoản in dưới đáy router), vào mục **DHCP / Address Reservation (Static DHCP)** và gán cố định địa chỉ đang hiện ở dòng *Trong mạng nhà* cho máy này.
2. **Chuyển cổng (Port Forwarding / NAT / Virtual Server):** thêm một dòng:
   - Giao thức: **TCP**
   - Cổng ngoài (External/WAN port): `5080` (hoặc cổng khác tùy chọn, ví dụ `8080`)
   - IP trong (Internal IP): địa chỉ của máy này, ví dụ `192.168.1.10`
   - Cổng trong (Internal port): `5080`
3. Trong app bấm **🌍 Xem địa chỉ Internet**, ví dụ `http://113.22.33.44:5080`. Mở địa chỉ đó từ mạng khác, ví dụ 4G trên điện thoại (thử từ cùng Wi-Fi nhiều router sẽ không mở được).
   Nếu ở bước 2 chọn cổng ngoài khác `5080`, thay số cổng tương ứng.

### Nếu vẫn không mở được từ xa

- **Nhà mạng dùng NAT nhiều lớp (CGNAT):** so địa chỉ WAN trong trang router với địa chỉ app hiện. Nếu khác nhau, hoặc WAN có dạng `100.64.x.x`–`100.127.x.x` hay `10.x.x.x`, thì chuyển cổng trên router nhà sẽ không có tác dụng. Khi đó gọi tổng đài nhà mạng (Viettel, FPT, VNPT…) xin **IP public (IP tĩnh/IP động public)**, hoặc dùng dịch vụ đường hầm như Tailscale hay Cloudflare Tunnel.
- **Địa chỉ Internet thay đổi:** phần lớn gói cước gia đình đổi IP sau mỗi lần router khởi động lại. Có thể bật **DDNS** trong router (No-IP, DynDNS…) để có một tên cố định.
- Máy tính phải **đang bật và đang mở app**: tắt app là trang web tắt.

## 4. https (mã hóa, và đăng nhập Google từ xa)

Google không cho đăng nhập trên trang mở bằng địa chỉ IP hay bằng `http`. App có sẵn https miễn phí theo tên miền **`<IP WAN có gạch ngang>.sslip.io`**: tên này luôn trỏ về IP WAN, không phải đăng ký. Ví dụ IP `14.241.68.10` thì tên miền là `14-241-68-10.sslip.io`.

1. Trên router, chuyển thêm **cổng 80 và 443 (TCP)** về máy chạy app, cùng cách với cổng `5080` ở mục 3. Ví dụ cổng ngoài `443` vào cổng trong `443`.
2. Trong app vào **Cài đặt → Trang web học**, tích **Bật https**. Để trống ô tên miền thì app tự điền theo IP WAN; có tên miền riêng (DuckDNS, No-IP…) thì gõ vào rồi bấm **Áp dụng**.
3. Bấm **🛡 Mở cổng tường lửa**: Windows sẽ cho phép cả cổng 80 và 443.
4. Mở `https://14-241-68-10.sslip.io` (thay bằng tên miền của bạn) từ mạng khác. Lần đầu, app tự xin chứng chỉ Let's Encrypt miễn phí (khoảng 1 phút) và tự gia hạn sau đó.
5. Để đăng nhập Google qua địa chỉ này: trong Google Cloud → client OAuth, thêm `https://14-241-68-10.sslip.io` vào **Authorized JavaScript origins** và `https://14-241-68-10.sslip.io/api/oauth/google/callback` vào **Authorized redirect URIs**.

Lưu ý:
- Không gõ `https://` với cổng `5080`: cổng đó chỉ chạy `http`. https luôn ở cổng 443, tức địa chỉ không ghi số cổng.
- IP WAN đổi thì app tự đổi tên `…sslip.io` theo IP mới (kiểm tra mỗi 10 phút), khởi động lại trang web và xin chứng chỉ mới. Thẻ **Trang web học** báo địa chỉ mới: nhớ sửa địa chỉ đó trong Google Cloud. Muốn tên cố định thì dùng DDNS (DuckDNS miễn phí) và gõ tên đó vào ô tên miền.
- Thẻ **Trang web học** hiện trạng thái chứng chỉ: 🔒 hợp lệ đến ngày nào, ⏳ đang xin, hoặc ⚠ lý do Let's Encrypt chưa cấp (thường do router chưa chuyển cổng 80/443).
- Cổng 80 hoặc 443 đang bị chương trình khác dùng (IIS, máy chủ web khác) thì app báo lỗi; tắt chương trình đó hoặc tắt https.
- Một số nhà mạng chặn cổng 80/443 cho gói gia đình; khi đó Let's Encrypt không cấp được chứng chỉ.

## Lưu ý an toàn

- Trang web ở cổng `5080` chạy bằng `http` (không mã hóa). Mở ra Internet thì nên bật https (mục 4) và dùng địa chỉ https. Dù vậy, hãy dùng **mật khẩu riêng cho trang học**, không trùng mật khẩu email hay ngân hàng. Không cần dùng từ xa nữa thì xóa dòng chuyển cổng trên router.
- Nhập sai mật khẩu 5 lần liên tiếp thì tài khoản bị khóa 1 phút.
- **Đăng nhập bằng Google** chỉ dùng được trên chính máy này (`http://localhost`) hoặc qua địa chỉ https có tên miền (mục 4). Google không cho đăng nhập trên trang `http://<địa chỉ IP>`.
- Bỏ tích **Cho máy khác mở trang web** thì chỉ máy này mở được trang web.
