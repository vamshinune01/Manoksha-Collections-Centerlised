import 'package:flutter/material.dart';

import '../api.dart';
import '../app_state.dart';
import 'common.dart';

/// Staff sign-in with their own work account (SPEC §5.1). Tokens are for the POS audience only.
class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _email = TextEditingController();
  final _password = TextEditingController();
  final _code = TextEditingController();
  final _newPassword = TextEditingController();
  String? _challenge;
  String _step = 'login'; // login | mfa | change
  bool _busy = false;

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
    } catch (e) {
      if (mounted) showError(context, e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _handle(Map<String, dynamic> r) async {
    switch (r['status'] as String) {
      case 'AUTHENTICATED':
        await TokenStore.save(r['tokens'] as Map<String, dynamic>);
        await AppState.instance.refreshContext();
      case 'MFA_REQUIRED':
        setState(() {
          _challenge = r['challengeToken'] as String;
          _step = 'mfa';
        });
      case 'PASSWORD_CHANGE_REQUIRED':
        setState(() {
          _challenge = r['challengeToken'] as String;
          _step = 'change';
        });
      case 'MFA_ENROLLMENT_REQUIRED':
        throw ApiException(403, 'MFA_ENROLLMENT_REQUIRED', 'Set up your authenticator app on the admin website first, then sign in here.');
      default:
        throw ApiException(400, 'UNKNOWN', 'Sign-in could not be completed.');
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 420),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  'MANOKSHA COLLECTIONS',
                  textAlign: TextAlign.center,
                  style: TextStyle(letterSpacing: 4, color: Theme.of(context).colorScheme.primary),
                ),
                const SizedBox(height: 8),
                const Text(
                  'Store POS',
                  textAlign: TextAlign.center,
                  style: TextStyle(fontSize: 26, fontWeight: FontWeight.w600),
                ),
                const SizedBox(height: 32),
                if (_step == 'login') ...[
                  TextField(
                    controller: _email,
                    decoration: const InputDecoration(labelText: 'Work email'),
                    keyboardType: TextInputType.emailAddress,
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _password,
                    decoration: const InputDecoration(labelText: 'Password'),
                    obscureText: true,
                  ),
                  const SizedBox(height: 20),
                  FilledButton(
                    onPressed: _busy
                        ? null
                        : () => _run(
                            () async => _handle(
                              await Api.instance.post('auth/internal/login', {'email': _email.text.trim(), 'password': _password.text, 'client': 'pos'})
                                  as Map<String, dynamic>,
                            ),
                          ),
                    child: const Padding(padding: EdgeInsets.all(12), child: Text('Sign in')),
                  ),
                ],
                if (_step == 'mfa') ...[
                  const Text('Enter the 6-digit code from your authenticator app.'),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _code,
                    decoration: const InputDecoration(labelText: 'Code'),
                    keyboardType: TextInputType.number,
                    maxLength: 6,
                  ),
                  FilledButton(
                    onPressed: _busy
                        ? null
                        : () => _run(
                            () async => _handle(
                              await Api.instance.post('auth/internal/mfa/verify', {'challengeToken': _challenge, 'code': _code.text.trim()})
                                  as Map<String, dynamic>,
                            ),
                          ),
                    child: const Text('Verify'),
                  ),
                ],
                if (_step == 'change') ...[
                  const Text('Choose your own password (at least 10 characters, with a letter and a digit).'),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _newPassword,
                    decoration: const InputDecoration(labelText: 'New password'),
                    obscureText: true,
                  ),
                  const SizedBox(height: 12),
                  FilledButton(
                    onPressed: _busy
                        ? null
                        : () => _run(
                            () async => _handle(
                              await Api.instance.post('auth/internal/password/first-change', {'challengeToken': _challenge, 'newPassword': _newPassword.text})
                                  as Map<String, dynamic>,
                            ),
                          ),
                    child: const Text('Save password'),
                  ),
                ],
                if (_busy) const Busy(),
                const SizedBox(height: 24),
                const Text(
                  'Internet is required to complete sales.',
                  textAlign: TextAlign.center,
                  style: TextStyle(color: Colors.black54),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
