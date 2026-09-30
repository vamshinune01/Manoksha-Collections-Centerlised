import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;

import 'config.dart';

/// A backend error with the API's machine-readable code (e.g. APPROVAL_REQUIRED) and a message for staff.
class ApiException implements Exception {
  ApiException(this.status, this.code, this.message, [this.details = const {}]);

  final int status;
  final String code;
  final String message;
  final Map<String, dynamic> details;

  /// True when the request never reached the server: the sale was NOT recorded (SPEC §23 — no offline sales).
  bool get isNetwork => code == 'NETWORK';

  @override
  String toString() => message;
}

/// Tokens live only in the device's secure storage (Android Keystore), never in plain preferences.
class TokenStore {
  static const _storage = FlutterSecureStorage();

  static Future<String?> access() => _storage.read(key: 'at');
  static Future<String?> refresh() => _storage.read(key: 'rt');

  static Future<void> save(Map<String, dynamic> tokens) async {
    await _storage.write(key: 'at', value: tokens['accessToken'] as String);
    await _storage.write(key: 'rt', value: tokens['refreshToken'] as String);
  }

  static Future<void> clear() => _storage.deleteAll();
}

/// Calls the central backend only. Refreshes the access token once on 401.
class Api {
  Api._();
  static final Api instance = Api._();

  final _client = http.Client();
  void Function()? onSignedOut;

  Future<dynamic> get(String path) => _send('GET', path);

  Future<dynamic> post(String path, [Object? body, Map<String, String>? headers]) => _send('POST', path, body, headers);

  Future<dynamic> put(String path, [Object? body]) => _send('PUT', path, body);

  Future<dynamic> _send(String method, String path, [Object? body, Map<String, String>? headers, bool retried = false]) async {
    final token = await TokenStore.access();
    final request = http.Request(method, Uri.parse('$apiBaseUrl/api/v1/$path'))
      ..headers.addAll({
        'Accept': 'application/json',
        if (body != null) 'Content-Type': 'application/json',
        if (token != null) 'Authorization': 'Bearer $token',
        ...?headers,
      });
    if (body != null) request.body = jsonEncode(body);

    http.Response response;
    try {
      response = await http.Response.fromStream(await _client.send(request).timeout(const Duration(seconds: 45)));
    } on SocketException {
      throw ApiException(0, 'NETWORK', 'No internet connection. Nothing was saved — try again when connected.');
    } on TimeoutException {
      throw ApiException(0, 'NETWORK', 'The server did not answer. Check the connection and try again — a retry will not charge twice.');
    } on http.ClientException {
      throw ApiException(0, 'NETWORK', 'No internet connection. Nothing was saved — try again when connected.');
    }

    if (response.statusCode == 401 && !retried && path != 'auth/refresh' && await _refresh()) {
      return _send(method, path, body, headers, true);
    }
    final text = utf8.decode(response.bodyBytes);
    final json = text.isEmpty ? null : jsonDecode(text);
    if (response.statusCode >= 200 && response.statusCode < 300) return json;
    if (response.statusCode == 401) {
      await TokenStore.clear();
      onSignedOut?.call();
    }
    final map = json is Map<String, dynamic> ? json : <String, dynamic>{};
    throw ApiException(
      response.statusCode,
      (map['code'] ?? 'ERROR') as String,
      (map['title'] ?? 'Something went wrong (${response.statusCode}).') as String,
      map,
    );
  }

  Future<bool> _refresh() async {
    final refresh = await TokenStore.refresh();
    if (refresh == null) return false;
    try {
      final response = await _client.post(
        Uri.parse('$apiBaseUrl/api/v1/auth/refresh'),
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({'refreshToken': refresh}),
      );
      if (response.statusCode != 200) return false;
      await TokenStore.save(jsonDecode(response.body) as Map<String, dynamic>);
      return true;
    } catch (_) {
      return false;
    }
  }
}
