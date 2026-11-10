package com.tubawinui3.remote.ui

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import com.tubawinui3.remote.RemoteViewModel
import com.tubawinui3.remote.data.ConnectLink
import com.tubawinui3.remote.data.ConnectTarget
import com.tubawinui3.remote.data.DEFAULT_PORT

/**
 * 「添加电脑 / 输入配对码」底部面板：扫码或手动 IP + 配对码。
 * [initial] 不为空时预填地址（列表里点未配对的电脑 / 配对失效时用）。
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AddPcSheet(vm: RemoteViewModel, initial: ConnectTarget?, onDismiss: () -> Unit) {
    val state = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    var address by remember(initial) { mutableStateOf(initial?.let { "${it.host}:${it.port}" } ?: "") }
    var code by remember(initial) { mutableStateOf("") }

    val scanner = rememberLauncherForActivityResult(ScanContract()) { result ->
        val text = result.contents ?: return@rememberLauncherForActivityResult
        val target = ConnectLink.parse(text)
        if (target == null) vm.scanError("不是有效的图吧工具箱连接二维码")
        else if (target.code != null) vm.connect(target, target.code)
        else address = "${target.host}:${target.port}"
    }

    // 配对成功 → 自动收起面板回到连接好的界面
    LaunchedEffect(vm.connected) {
        if (vm.connected) onDismiss()
    }

    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = state) {
        Column(
            Modifier.fillMaxWidth().padding(horizontal = 24.dp).padding(bottom = 24.dp).navigationBarsPadding(),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(initial?.name?.let { "连接 $it" } ?: "添加电脑", style = MaterialTheme.typography.headlineSmall)
            Text(
                "在电脑上打开 图吧工具箱 → 内置工具 → 连接手机，并确保手机与电脑在同一局域网。",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Button(
                onClick = {
                    scanner.launch(
                        ScanOptions().setDesiredBarcodeFormats(ScanOptions.QR_CODE).setPrompt("扫描电脑上的二维码")
                            .setBeepEnabled(false).setOrientationLocked(false),
                    )
                },
                enabled = !vm.connecting,
                modifier = Modifier.fillMaxWidth(),
            ) { Text("扫码连接") }

            Text("或手动输入", style = MaterialTheme.typography.labelLarge)
            OutlinedTextField(
                value = address, onValueChange = { address = it.trim() },
                label = { Text("电脑完整连接信息（含证书指纹），或扫码连接") },
                singleLine = true, modifier = Modifier.fillMaxWidth(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
            )
            OutlinedTextField(
                value = code, onValueChange = { code = it.filter(Char::isDigit).take(6) },
                label = { Text("6 位配对码（成功后保存以便自动连接）") },
                visualTransformation = PasswordVisualTransformation(),
                singleLine = true, modifier = Modifier.fillMaxWidth(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
            )
            vm.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                OutlinedButton(
                    onClick = {
                        val t: ConnectTarget? = ConnectLink.parse(address)?.let { it.copy(fingerprint = it.fingerprint ?: initial?.fingerprint) }
                        if (t == null) vm.scanError("请输入正确的 IP 地址") else vm.connect(t, code)
                    },
                    enabled = !vm.connecting,
                ) { Text("连接") }
                if (vm.connecting) CircularProgressIndicator(Modifier.height(36.dp))
            }
            Spacer(Modifier.height(8.dp))
        }
    }
}
